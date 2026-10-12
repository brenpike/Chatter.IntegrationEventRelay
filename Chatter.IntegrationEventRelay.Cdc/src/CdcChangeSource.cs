using Chatter.IntegrationEventRelay.Core.Abstractions;

namespace Chatter.IntegrationEventRelay.Cdc;

/// <summary>
/// A change source that polls SQL Server Change Data Capture (CDC) change tables.
/// </summary>
/// <remarks>
/// Changes are delivered in commit order, in batches of whole transactions. A batch is acknowledged by saving its last
/// LSN as the capture instance's checkpoint once every change in it is published; after a failure or restart the batch
/// is read again and republished with the same message ids, so broker duplicate detection can drop the replays.
/// </remarks>
public sealed class CdcChangeSource : IChangeSource
{
	public const string SourceType = "Cdc";

	public CdcChangeSource(string name) => Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentNullException(nameof(name)) : name;

	public string Name { get; }
	public string Type => SourceType;
	public ChangeSourceCapabilities Capabilities => ChangeSourceCapabilities.Ordered | ChangeSourceCapabilities.Resumable | ChangeSourceCapabilities.CommitTime;
}
