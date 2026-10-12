using Chatter.IntegrationEventRelay.Core.Abstractions;

namespace Chatter.IntegrationEventRelay.Cdc;

/// <summary>
/// A change source that polls SQL Server Change Data Capture (CDC) change tables.
/// </summary>
/// <remarks>
/// Each capture instance (table) is polled on its own, and its changes are delivered in commit order, in batches of
/// whole transactions; there's no order across tables. A batch is acknowledged by saving its last
/// LSN as the capture instance's checkpoint once every change in it is published; after a failure or restart the batch
/// is read again and republished with the same message ids, so broker duplicate detection can drop the replays.
/// <para>
/// Commit times come from <c>cdc.lsn_time_mapping</c>, which records them in the server's local time, and are given the
/// server's UTC offset when the batch is read. If the server's time zone observes daylight saving time, changes
/// committed before a clock change but read after it are off by the change; run SQL Server in UTC to avoid it.
/// </para>
/// <para>
/// Permissions: the relay's login needs <c>SELECT</c> on the <c>cdc</c> schema, since it reads the change tables
/// directly (the only place SQL Server exposes <c>__$command_id</c>, which orders a transaction's changes) and CDC's
/// metadata tables; and <c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c> on the relay's
/// <see cref="Core.Configuration.EventMappingConfiguration.Schema"/>. See <c>.scripts/cdc/enable-cdc.sql</c>.
/// </para>
/// </remarks>
public sealed class CdcChangeSource : IChangeSource
{
	public const string SourceType = "Cdc";

	/// <param name="errorPolicy">The source's error policy. <see cref="CdcErrorPolicy.ParkRow"/> publishes later changes
	/// to other rows before a parked one, so the source is then only ordered per row and doesn't claim <see cref="ChangeSourceCapabilities.Ordered"/>.</param>
	public CdcChangeSource(string name, CdcErrorPolicy errorPolicy = CdcErrorPolicy.Halt)
	{
		Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentNullException(nameof(name)) : name;
		Capabilities = ChangeSourceCapabilities.Resumable | ChangeSourceCapabilities.CommitTime
			| (errorPolicy == CdcErrorPolicy.ParkRow ? ChangeSourceCapabilities.None : ChangeSourceCapabilities.Ordered);
	}

	public string Name { get; }
	public string Type => SourceType;
	public ChangeSourceCapabilities Capabilities { get; }
}
