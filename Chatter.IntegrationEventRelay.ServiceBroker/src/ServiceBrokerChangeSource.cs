using Chatter.IntegrationEventRelay.Core.Abstractions;

namespace Chatter.IntegrationEventRelay.ServiceBroker;

/// <summary>
/// A change source that captures row changes with triggers that send them to a SQL Service Broker queue (Chatter.SqlChangeFeed).
/// </summary>
/// <remarks>
/// Changes are acknowledged by committing Chatter's receive transaction once processing succeeds; failures are retried
/// by Chatter's recovery options. Delivery is at least once, without ordering guarantees across receivers, change
/// positions or commit times.
/// </remarks>
public sealed class ServiceBrokerChangeSource : IChangeSource
{
	public const string SourceType = "ServiceBroker";

	public ServiceBrokerChangeSource(string name) => Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentNullException(nameof(name)) : name;

	public string Name { get; }
	public string Type => SourceType;
	public ChangeSourceCapabilities Capabilities => ChangeSourceCapabilities.None;
}
