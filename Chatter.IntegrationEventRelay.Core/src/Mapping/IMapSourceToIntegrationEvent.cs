using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.MessageBrokers.Context;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>
	where TSourceEvent : class, ISourceEvent
	where TIntegrationEvent : IEvent?
{
	TIntegrationEvent? Map(MappingData<TSourceEvent> mappingData, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig) => MapAsync(mappingData, context, mappingConfig).GetAwaiter().GetResult();
	Task<TIntegrationEvent?> MapAsync(MappingData<TSourceEvent> mappingData, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig);
}
