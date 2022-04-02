using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.MessageBrokers.Context;

namespace Chatter.IntegrationEventRelay.Core;

public interface IRelayIntegrationEvent
{
    public Task RelayAsync<TIntegrationEvent>(TIntegrationEvent? @event, IMessageBrokerContext context, EventMappingConfigurationItem settings)
        where TIntegrationEvent : class, IEvent;
}
