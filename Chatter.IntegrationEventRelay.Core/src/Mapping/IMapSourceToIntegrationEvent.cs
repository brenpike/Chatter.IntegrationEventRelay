using Chatter.CQRS.Events;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    TIntegrationEvent Map(MappingData<TSourceEvent> mappingData) => MapAsync(mappingData).GetAwaiter().GetResult();
    Task<TIntegrationEvent> MapAsync(MappingData<TSourceEvent> mappingData);
}
