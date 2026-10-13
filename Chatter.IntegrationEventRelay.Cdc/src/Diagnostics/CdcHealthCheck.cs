using Chatter.IntegrationEventRelay.Cdc.Polling;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chatter.IntegrationEventRelay.Cdc.Diagnostics;

/// <summary>
/// Reports the CDC pollers of this relay instance. Unhealthy when a poller is stopped on a gap, has failed
/// <see cref="CdcSourceOptions.FailuresBeforeCritical"/> times in a row, or lags more than the allowed time; degraded
/// while a batch is being retried or changes are parked. Standby instances are healthy.
/// </summary>
internal sealed class CdcHealthCheck : IHealthCheck
{
	private readonly IEnumerable<ICdcCapturePoller> _pollers;
	private readonly TimeProvider _timeProvider;
	private readonly TimeSpan _maxLag;

	public CdcHealthCheck(IEnumerable<ICdcCapturePoller> pollers, TimeProvider timeProvider, TimeSpan maxLag)
	{
		_pollers = pollers ?? throw new ArgumentNullException(nameof(pollers));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_maxLag = maxLag;
	}

	public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		var now = _timeProvider.GetUtcNow();
		var data = new Dictionary<string, object>();
		var unhealthy = new List<string>();
		var degraded = new List<string>();

		foreach (var poller in _pollers)
		{
			var name = $"{poller.Capture.SourceName}/{poller.Capture.CaptureInstance}";
			var lag = poller.GetLag(now);
			data[name] = new { State = poller.State.ToString(), poller.ConsecutiveFailures, LagSeconds = lag?.TotalSeconds, poller.ParkedChanges };

			if (poller.State == CdcPollerState.Gap)
				unhealthy.Add($"{name}: changes were removed by CDC cleanup before they were relayed");
			else if (poller.State == CdcPollerState.Retrying && poller.ConsecutiveFailures >= poller.Capture.Options.FailuresBeforeCritical)
				unhealthy.Add($"{name}: halted after {poller.ConsecutiveFailures} failed attempts");
			else if (poller.State == CdcPollerState.Retrying)
				degraded.Add($"{name}: retrying a failed batch");

			// A standby or starting instance doesn't own the capture; its lag and parked count may be left over from
			// before it lost the lease, and the active instance reports the real ones
			if (poller.State is not (CdcPollerState.Active or CdcPollerState.Retrying or CdcPollerState.Gap))
				continue;

			if (lag > _maxLag)
				unhealthy.Add($"{name}: {lag.Value.TotalSeconds:0}s behind");
			if (poller.ParkedChanges > 0)
				degraded.Add($"{name}: {poller.ParkedChanges} parked change(s)");
		}

		var result = unhealthy.Count > 0
			? HealthCheckResult.Unhealthy(string.Join("; ", unhealthy.Concat(degraded)), data: data)
			: degraded.Count > 0
				? HealthCheckResult.Degraded(string.Join("; ", degraded), data: data)
				: HealthCheckResult.Healthy("CDC change sources are relaying", data);
		return Task.FromResult(result);
	}
}
