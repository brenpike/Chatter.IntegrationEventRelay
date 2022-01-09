using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;

namespace Chatter.IntegrationEventRelay.Core;

public interface IRelayIntegrationEvent
{
    public Task Relay<TIntegrationEvent>(TIntegrationEvent? @event, IMessageHandlerContext context, EventMappingConfigurationItem settings)
        where TIntegrationEvent : class, IEvent;
}
