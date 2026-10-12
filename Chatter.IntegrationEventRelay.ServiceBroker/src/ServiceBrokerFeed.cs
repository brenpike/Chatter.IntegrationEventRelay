namespace Chatter.IntegrationEventRelay.ServiceBroker;

/// <summary>
/// A source event type with a SqlChangeFeed, recorded so its database objects can be installed at startup.
/// </summary>
public sealed record ServiceBrokerFeed(Type SourceEventType);
