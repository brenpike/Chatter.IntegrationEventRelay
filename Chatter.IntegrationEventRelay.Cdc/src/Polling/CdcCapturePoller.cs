using Chatter.IntegrationEventRelay.Cdc.Reading;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Cdc.Polling;

/// <summary>
/// The poll loop of one capture instance: while holding its lease, reads the changes after the checkpoint in batches
/// of whole transactions, processes each batch in commit order and then saves the checkpoint.
/// </summary>
/// <remarks>
/// A failed batch is retried with backoff and nothing after it is published, so ordering holds; it isn't checkpointed,
/// so every change in it is published at least once.
/// </remarks>
internal sealed class CdcCapturePoller<TSource> : ICdcCapturePoller where TSource : class, new()
{
	private readonly ICdcQueries _queries;
	private readonly ICdcCheckpointStore _checkpoints;
	private readonly ICdcLeaseFactory _leases;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<CdcCapturePoller<TSource>> _logger;
	private readonly CdcRowReader<TSource> _reader;
	private bool _columnsChecked;

	public CdcCapturePoller(CdcCapture capture, ICdcQueries queries, ICdcCheckpointStore checkpoints, ICdcLeaseFactory leases,
		IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<CdcCapturePoller<TSource>> logger)
	{
		Capture = capture ?? throw new ArgumentNullException(nameof(capture));
		_queries = queries ?? throw new ArgumentNullException(nameof(queries));
		_checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));
		_leases = leases ?? throw new ArgumentNullException(nameof(leases));
		_scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_reader = new CdcRowReader<TSource>(logger);
	}

	public CdcCapture Capture { get; }

	public CdcPollerState State { get; private set; } = CdcPollerState.Starting;

	private CdcSourceOptions Options => Capture.Options;

	public async Task RunAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await using var lease = _leases.Create(Capture.LeaseResource);
				if (!await TryAcquireAsync(lease, stoppingToken).ConfigureAwait(false))
				{
					State = CdcPollerState.Standby;
					await DelayAsync(Options.LeaseRetryInterval, stoppingToken).ConfigureAwait(false);
					continue;
				}

				_logger.LogInformation("Polling capture instance '{CaptureInstance}' of source '{SourceName}'", Capture.CaptureInstance, Capture.SourceName);
				try
				{
					await PollWhileLeasedAsync(lease, stoppingToken).ConfigureAwait(false);
				}
				catch (CdcLeaseLostException)
				{
					_logger.LogWarning("Lost the lease on capture instance '{CaptureInstance}' of source '{SourceName}'; another relay instance may take over", Capture.CaptureInstance, Capture.SourceName);
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}

		_logger.LogInformation("Stopped polling capture instance '{CaptureInstance}' of source '{SourceName}'", Capture.CaptureInstance, Capture.SourceName);
	}

	private async Task<bool> TryAcquireAsync(ICdcLease lease, CancellationToken stoppingToken)
	{
		try
		{
			var acquired = await lease.TryAcquireAsync(stoppingToken).ConfigureAwait(false);
			if (!acquired)
				_logger.LogDebug("Capture instance '{CaptureInstance}' of source '{SourceName}' is polled by another relay instance; standing by", Capture.CaptureInstance, Capture.SourceName);
			return acquired;
		}
		catch (Exception e) when (!stoppingToken.IsCancellationRequested)
		{
			_logger.LogError(e, "Couldn't take the lease on capture instance '{CaptureInstance}' of source '{SourceName}'", Capture.CaptureInstance, Capture.SourceName);
			return false;
		}
	}

	private async Task PollWhileLeasedAsync(ICdcLease lease, CancellationToken stoppingToken)
	{
		var failures = 0;
		while (true)
		{
			stoppingToken.ThrowIfCancellationRequested();
			bool processedBatch;
			try
			{
				if (!await lease.IsHeldAsync(stoppingToken).ConfigureAwait(false))
					throw new CdcLeaseLostException(Capture.CaptureInstance);

				processedBatch = await PollOnceAsync(stoppingToken).ConfigureAwait(false);
				if (failures > 0)
					_logger.LogInformation("Capture instance '{CaptureInstance}' of source '{SourceName}' recovered after {FailureCount} failed attempt(s)", Capture.CaptureInstance, Capture.SourceName, failures);
				failures = 0;
				State = CdcPollerState.Active;
			}
			catch (Exception e) when (e is not CdcLeaseLostException && !(e is OperationCanceledException && stoppingToken.IsCancellationRequested))
			{
				failures++;
				State = e is CdcGapException ? CdcPollerState.Gap : CdcPollerState.Retrying;
				var delay = GetRetryDelay(failures);
				var level = e is CdcGapException || failures >= Options.FailuresBeforeCritical ? LogLevel.Critical : LogLevel.Error;
				_logger.Log(level, e, "Polling capture instance '{CaptureInstance}' of source '{SourceName}' failed ({FailureCount} in a row); nothing after the failed batch is published. Retrying in {RetryDelay}",
					Capture.CaptureInstance, Capture.SourceName, failures, delay);
				await DelayAsync(delay, stoppingToken).ConfigureAwait(false);
				continue;
			}

			if (!processedBatch)
				await DelayAsync(Options.PollInterval, stoppingToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Reads, processes and checkpoints the next batch.
	/// </summary>
	/// <returns>True if it moved the checkpoint, so more changes may be waiting; false if there was nothing to read.</returns>
	internal async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
	{
		var minLsn = await _queries.GetMinLsnAsync(Capture.CaptureInstance, cancellationToken).ConfigureAwait(false);
		if (minLsn.IsZero)
			throw new CdcCaptureInstanceNotFoundException(Capture.SourceName, Capture.CaptureInstance);

		await WarnAboutUnmappedPropertiesAsync(cancellationToken).ConfigureAwait(false);

		var maxLsn = await _queries.GetMaxLsnAsync(cancellationToken).ConfigureAwait(false);
		var checkpoint = await _checkpoints.GetAsync(Capture.SourceName, Capture.CaptureInstance, cancellationToken).ConfigureAwait(false);

		CdcLsn from;
		if (checkpoint is { } last)
		{
			from = last.Increment();
			if (from < minLsn)
				throw new CdcGapException(Capture.SourceName, Capture.CaptureInstance, last, minLsn);
		}
		else if (Options.StartPosition == CdcStartPosition.Latest)
		{
			if (maxLsn.IsZero)
				return false;

			// Anchor the start now, so changes committed while the relay is down later aren't skipped
			await _checkpoints.SaveAsync(Capture.SourceName, Capture.CaptureInstance, maxLsn, cancellationToken).ConfigureAwait(false);
			_logger.LogInformation("Capture instance '{CaptureInstance}' of source '{SourceName}' has no checkpoint; starting after the latest change, LSN {Lsn}", Capture.CaptureInstance, Capture.SourceName, maxLsn);
			return true;
		}
		else
		{
			from = minLsn;
		}

		if (maxLsn.IsZero || from > maxLsn)
			return false;

		var to = await _queries.GetBatchUpperBoundAsync(from, maxLsn, Options.MaxTransactionsPerBatch, cancellationToken).ConfigureAwait(false);
		if (to is not { } upperBound)
			return false;

		var rows = await _queries.GetChangesAsync(Capture.CaptureInstance, from, upperBound, cancellationToken).ConfigureAwait(false);
		if (rows.Count > 0)
		{
			var offset = await _queries.GetServerUtcOffsetAsync(cancellationToken).ConfigureAwait(false);
			var changes = _reader.Read(rows, Capture, offset);

			// The publisher is scoped (Chatter's dispatcher), so each batch gets its own scope
			await using var scope = _scopeFactory.CreateAsyncScope();
			var processor = scope.ServiceProvider.GetRequiredService<IRowChangeProcessor<TSource>>();
			await processor.ProcessBatchAsync(changes, cancellationToken).ConfigureAwait(false);
			_logger.LogInformation("Relayed {ChangeCount} change(s) from capture instance '{CaptureInstance}' of source '{SourceName}' up to LSN {Lsn}", changes.Count, Capture.CaptureInstance, Capture.SourceName, upperBound);
		}

		// Only after every change in the batch is published; a failure above leaves the checkpoint where it was
		await _checkpoints.SaveAsync(Capture.SourceName, Capture.CaptureInstance, upperBound, cancellationToken).ConfigureAwait(false);
		return true;
	}

	private async Task WarnAboutUnmappedPropertiesAsync(CancellationToken cancellationToken)
	{
		if (_columnsChecked)
			return;

		var columns = await _queries.GetCapturedColumnsAsync(Capture.CaptureInstance, cancellationToken).ConfigureAwait(false);
		var unmapped = _reader.GetUnmappedProperties(columns);
		if (unmapped.Count > 0)
			_logger.LogWarning("Capture instance '{CaptureInstance}' doesn't capture a column for properties {UnmappedProperties} of '{SourceEventTypeName}'; they keep their default values",
				Capture.CaptureInstance, string.Join(", ", unmapped), typeof(TSource).Name);
		_columnsChecked = true;
	}

	internal TimeSpan GetRetryDelay(int failures)
	{
		var ticks = Options.RetryDelay.Ticks * Math.Pow(2, Math.Min(failures - 1, 30));
		return TimeSpan.FromTicks((long)Math.Min(ticks, Options.MaxRetryDelay.Ticks));
	}

	private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, _timeProvider, cancellationToken);
}
