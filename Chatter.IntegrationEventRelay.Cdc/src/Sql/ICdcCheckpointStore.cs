namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// The last LSN a CDC source has fully published for a capture instance.
/// </summary>
internal interface ICdcCheckpointStore
{
	Task<CdcLsn?> GetAsync(string sourceName, string captureInstance, CancellationToken cancellationToken);

	/// <summary>
	/// Saves <paramref name="lastLsn"/> unless the stored checkpoint is already at or past it.
	/// </summary>
	Task SaveAsync(string sourceName, string captureInstance, CdcLsn lastLsn, CancellationToken cancellationToken);
}
