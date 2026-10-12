using Chatter.IntegrationEventRelay.Cdc.Polling;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Chatter.IntegrationEventRelay.Cdc.Diagnostics;

/// <summary>
/// Metrics of CDC change sources, on meter <see cref="MeterName"/>. Every measurement is tagged with
/// <c>source</c> and <c>capture_instance</c>.
/// </summary>
public static class CdcMetrics
{
	public const string MeterName = "Chatter.IntegrationEventRelay.Cdc";

	private static readonly Meter Meter = new(MeterName);
	private static readonly ConcurrentDictionary<ICdcCapturePoller, byte> Pollers = new();

	private static readonly Counter<long> ChangesRelayed = Meter.CreateCounter<long>("chatter.relay.cdc.changes", "{change}", "Changes relayed (published or, with ParkRow, parked).");
	private static readonly Counter<long> ChangesParked = Meter.CreateCounter<long>("chatter.relay.cdc.parked", "{change}", "Changes parked by the ParkRow error policy.");
	private static readonly Counter<long> BatchFailures = Meter.CreateCounter<long>("chatter.relay.cdc.batch.failures", "{batch}", "Failed attempts at a batch.");
	private static readonly Histogram<double> BatchDuration = Meter.CreateHistogram<double>("chatter.relay.cdc.batch.duration", "s", "Time to read, process and checkpoint a batch.");

	static CdcMetrics()
	{
		Meter.CreateObservableGauge("chatter.relay.cdc.lag", () => Observe(p => p.GetLag(DateTimeOffset.UtcNow)?.TotalSeconds), "s",
			"Time since the last relayed change was committed; 0 when caught up.");
		Meter.CreateObservableGauge("chatter.relay.cdc.parked.current", () => Observe(p => p.ParkedChanges is { } count ? (double?)count : null), "{change}",
			"Changes currently parked.");
	}

	internal static void Track(ICdcCapturePoller poller) => Pollers.TryAdd(poller, 0);

	internal static void Untrack(ICdcCapturePoller poller) => Pollers.TryRemove(poller, out _);

	internal static void RecordBatch(CdcCapture capture, int changes, int parked, TimeSpan duration)
	{
		var tags = Tags(capture);
		ChangesRelayed.Add(changes, tags);
		if (parked > 0)
			ChangesParked.Add(parked, tags);
		BatchDuration.Record(duration.TotalSeconds, tags);
	}

	internal static void RecordBatchFailure(CdcCapture capture) => BatchFailures.Add(1, Tags(capture));

	private static IEnumerable<Measurement<double>> Observe(Func<ICdcCapturePoller, double?> value)
		=> Pollers.Keys
			.Where(p => p.State == CdcPollerState.Active || p.State == CdcPollerState.Retrying || p.State == CdcPollerState.Gap)
			.Select(p => (Poller: p, Value: value(p)))
			.Where(m => m.Value is not null)
			.Select(m => new Measurement<double>(m.Value!.Value, Tags(m.Poller.Capture)));

	private static KeyValuePair<string, object?>[] Tags(CdcCapture capture)
		=> [new("source", capture.SourceName), new("capture_instance", capture.CaptureInstance)];
}
