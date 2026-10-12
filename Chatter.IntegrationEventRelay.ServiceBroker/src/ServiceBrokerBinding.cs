using Chatter.IntegrationEventRelay.Core.Abstractions;

namespace Chatter.IntegrationEventRelay.ServiceBroker;

/// <summary>
/// The source and table that changes to <typeparamref name="TSource"/> are captured from. SqlChangeFeed creates one
/// queue per source type, so each source type is watched in exactly one table.
/// </summary>
public sealed record ServiceBrokerBinding<TSource>(string SourceName, SourceTable Table) where TSource : class;
