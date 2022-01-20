using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;

namespace Chatter.IntegrationEventRelay.Core.Handlers;

public interface IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    Task Execute(IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent> integrationEventMapper,
                 MappingData<TSourceEvent> mapping,
                 EventMappingConfigurationItem? eventMappingConfiguration,
                 IMessageHandlerContext context);
}
