using Chatter.CQRS;

namespace Chatter.IntegrationEventRelay.Core;

/// <summary>
/// Optional marker for types that changed rows are mapped to.
/// </summary>
/// <remarks>
/// Change sources only require source types to be classes. The SQL Service Broker source (Chatter.SqlChangeFeed) also
/// requires <see cref="IMessage"/>, which this marker provides.
/// </remarks>
public interface ISourceEvent : IMessage
{
}
