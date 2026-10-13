namespace Chatter.IntegrationEventRelay.Cdc;

/// <summary>
/// CDC cleanup removed changes the relay hasn't published: the capture instance's oldest available LSN is past the
/// checkpoint. The poller stops on it until the checkpoint is reset (losing those changes is an operator's decision).
/// </summary>
public sealed class CdcGapException : Exception
{
	public CdcGapException(string sourceName, string captureInstance, CdcLsn checkpoint, CdcLsn minLsn)
		: base($"Source '{sourceName}' has published capture instance '{captureInstance}' up to LSN {checkpoint}, but its oldest available change is at LSN {minLsn}: " +
			"changes in between were removed by CDC cleanup before they were relayed. Increase the CDC retention period, then update or delete the checkpoint to resume.")
	{
		SourceName = sourceName;
		CaptureInstance = captureInstance;
		Checkpoint = checkpoint;
		MinLsn = minLsn;
	}

	public string SourceName { get; }
	public string CaptureInstance { get; }
	public CdcLsn Checkpoint { get; }
	public CdcLsn MinLsn { get; }
}

/// <summary>
/// The capture instance doesn't exist.
/// </summary>
public sealed class CdcCaptureInstanceNotFoundException : Exception
{
	public CdcCaptureInstanceNotFoundException(string sourceName, string captureInstance)
		: base($"Source '{sourceName}' can't find capture instance '{captureInstance}'. Is CDC enabled on the table, and does the mapping's CaptureInstance name it?")
	{
		SourceName = sourceName;
		CaptureInstance = captureInstance;
	}

	public string SourceName { get; }
	public string CaptureInstance { get; }
}

/// <summary>
/// The capture instance exists, but the relay's login may not read its changes.
/// </summary>
public sealed class CdcCaptureInstanceAccessDeniedException : Exception
{
	public CdcCaptureInstanceAccessDeniedException(string sourceName, string captureInstance)
		: base($"Source '{sourceName}' may not read capture instance '{captureInstance}'. The relay's login needs SELECT on every column the capture instance captures " +
			"(for example GRANT SELECT (<captured columns>) ON <schema>.<table>) and membership in its gating role, if it has one, as well as SELECT on its change table and CDC's metadata tables (see .scripts/cdc/enable-cdc.sql).")
	{
		SourceName = sourceName;
		CaptureInstance = captureInstance;
	}

	public string SourceName { get; }
	public string CaptureInstance { get; }
}

internal sealed class CdcLeaseLostException : Exception
{
	public CdcLeaseLostException(string captureInstance) : base($"Lost the lease on capture instance '{captureInstance}'.") { }
}
