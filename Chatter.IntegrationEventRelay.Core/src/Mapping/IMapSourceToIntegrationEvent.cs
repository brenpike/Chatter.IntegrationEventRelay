using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : IEvent?
{
    TIntegrationEvent? Map(MappingData<TSourceEvent> mappingData, EventMappingConfigurationItem? mappingConfig) => MapAsync(mappingData, mappingConfig).GetAwaiter().GetResult();
	Task<TIntegrationEvent?> MapAsync(MappingData<TSourceEvent> mappingData, EventMappingConfigurationItem? mappingConfig);
}
