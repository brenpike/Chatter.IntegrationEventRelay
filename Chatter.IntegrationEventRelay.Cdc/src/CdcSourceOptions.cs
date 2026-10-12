namespace Chatter.IntegrationEventRelay.Cdc;

/// <summary>
/// Settings of a CDC change source, read from its <c>IntegrationEventRelay:Sources:&lt;name&gt;</c> section.
/// </summary>
public class CdcSourceOptions
{
	/// <summary>
	/// How long to wait before polling again when there are no new changes. Default 1 second.
	/// </summary>
	public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The most source transactions read in one batch. A batch is checkpointed only once all of its changes are published. Default 500.
	/// </summary>
	public int MaxTransactionsPerBatch { get; set; } = 500;

	/// <summary>
	/// Where a capture instance with no checkpoint starts. Default <see cref="CdcStartPosition.Earliest"/>.
	/// </summary>
	public CdcStartPosition StartPosition { get; set; } = CdcStartPosition.Earliest;

	/// <summary>
	/// How long a standby relay waits before trying again to become the active poller of a capture instance. Default 15 seconds.
	/// </summary>
	public TimeSpan LeaseRetryInterval { get; set; } = TimeSpan.FromSeconds(15);

	/// <summary>
	/// The first wait after a failed batch; it doubles with each consecutive failure up to <see cref="MaxRetryDelay"/>. Default 1 second.
	/// </summary>
	public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The longest wait between retries of a failed batch. Default 1 minute.
	/// </summary>
	public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Consecutive failures of a batch after which the relay logs it as critical (the poller is halted on it). Default 5.
	/// </summary>
	public int FailuresBeforeCritical { get; set; } = 5;

	/// <summary>
	/// What to do with a change that keeps failing. Default <see cref="CdcErrorPolicy.Halt"/>.
	/// </summary>
	public CdcErrorPolicy ErrorPolicy { get; set; } = CdcErrorPolicy.Halt;

	/// <summary>
	/// With <see cref="CdcErrorPolicy.ParkRow"/>: the attempts at a change, failing with errors that aren't transient,
	/// before it's parked. Default 5.
	/// </summary>
	public int MaxAttemptsBeforePark { get; set; } = 5;

	/// <summary>
	/// Decides which failures are transient (for example the broker is unavailable). Transient failures are retried
	/// and never parked, since parking them would park every change during an outage. Default
	/// <see cref="CdcTransientErrors.IsTransient"/>.
	/// </summary>
	public Func<Exception, bool> IsTransient { get; set; } = CdcTransientErrors.IsTransient;

	internal IEnumerable<string> Validate()
	{
		if (PollInterval <= TimeSpan.Zero)
			yield return $"{nameof(PollInterval)} must be positive.";
		if (MaxTransactionsPerBatch <= 0)
			yield return $"{nameof(MaxTransactionsPerBatch)} must be positive.";
		if (LeaseRetryInterval <= TimeSpan.Zero)
			yield return $"{nameof(LeaseRetryInterval)} must be positive.";
		if (RetryDelay <= TimeSpan.Zero)
			yield return $"{nameof(RetryDelay)} must be positive.";
		if (MaxRetryDelay < RetryDelay)
			yield return $"{nameof(MaxRetryDelay)} must be at least {nameof(RetryDelay)}.";
		if (FailuresBeforeCritical <= 0)
			yield return $"{nameof(FailuresBeforeCritical)} must be positive.";
		if (MaxAttemptsBeforePark <= 0)
			yield return $"{nameof(MaxAttemptsBeforePark)} must be positive.";
		if (IsTransient is null)
			yield return $"{nameof(IsTransient)} is required.";
	}
}

/// <summary>
/// Where a capture instance with no checkpoint starts reading.
/// </summary>
public enum CdcStartPosition
{
	/// <summary>The oldest change still in the change table.</summary>
	Earliest,
	/// <summary>
	/// Changes captured after the relay's first poll of the capture instance; older changes are skipped. The boundary is
	/// the newest LSN read at the start of that poll, so a change committed at the moment the relay starts may fall on
	/// either side.
	/// </summary>
	Latest
}

/// <summary>
/// What a CDC source does with a change that keeps failing with errors that aren't transient.
/// </summary>
public enum CdcErrorPolicy
{
	/// <summary>
	/// Stop at the change and keep retrying it with backoff, logging critical. Nothing after it is published, so ordering always holds.
	/// </summary>
	Halt,
	/// <summary>
	/// After <see cref="CdcSourceOptions.MaxAttemptsBeforePark"/> attempts, park the change, and every later change to
	/// the same row behind it, in <c>&lt;Schema&gt;.Chatter_RelayParkedChanges</c>. Other rows carry on. Parked
	/// changes are republished in order once a replay is requested. Needs a table with a primary key or unique index.
	/// </summary>
	ParkRow
}
