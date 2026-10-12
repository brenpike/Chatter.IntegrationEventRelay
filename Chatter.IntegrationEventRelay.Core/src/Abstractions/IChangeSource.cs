namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// A configured source of row changes, such as SQL Service Broker or CDC. Each mapping reads from exactly one source.
/// </summary>
/// <remarks>
/// The source owns acknowledgement: it acknowledges a change only after <see cref="Processing.IRowChangeProcessor{TSource}"/> returns.
/// </remarks>
public interface IChangeSource
{
	/// <summary>
	/// The source name that mappings refer to.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// The source type, for example "ServiceBroker".
	/// </summary>
	string Type { get; }

	ChangeSourceCapabilities Capabilities { get; }
}
