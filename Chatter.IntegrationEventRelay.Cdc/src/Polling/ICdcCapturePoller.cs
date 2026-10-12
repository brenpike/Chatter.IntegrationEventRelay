namespace Chatter.IntegrationEventRelay.Cdc.Polling;

/// <summary>
/// Polls one capture instance until stopped.
/// </summary>
internal interface ICdcCapturePoller
{
	CdcCapture Capture { get; }

	CdcPollerState State { get; }

	/// <summary>Failed attempts at the current batch; 0 once a batch succeeds.</summary>
	int ConsecutiveFailures { get; }

	/// <summary>Changes parked for this capture instance, as of the last batch; null unless the error policy is ParkRow.</summary>
	int? ParkedChanges { get; }

	/// <summary>
	/// Time since the last relayed change was committed; zero when the last poll found nothing new; null before anything is relayed.
	/// </summary>
	TimeSpan? GetLag(DateTimeOffset now);

	Task RunAsync(CancellationToken stoppingToken);
}

internal enum CdcPollerState
{
	Starting,
	/// <summary>Another relay instance holds the capture instance's lease.</summary>
	Standby,
	/// <summary>Polling and publishing.</summary>
	Active,
	/// <summary>The last batch failed; retrying it with backoff. Nothing after it is published.</summary>
	Retrying,
	/// <summary>Changes were removed by CDC cleanup before they were published; waiting for the checkpoint to be reset.</summary>
	Gap
}
