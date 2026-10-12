namespace Chatter.IntegrationEventRelay.Cdc.Polling;

/// <summary>
/// Polls one capture instance until stopped.
/// </summary>
internal interface ICdcCapturePoller
{
	CdcCapture Capture { get; }

	CdcPollerState State { get; }

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
