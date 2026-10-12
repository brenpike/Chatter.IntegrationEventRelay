using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>
	where TSourceEvent : class
	where TIntegrationEvent : IEvent?
{
	/// <summary>
	/// Maps a changed row to an integration event.
	/// </summary>
	/// <returns>The integration event to publish, or null to publish nothing for this change.</returns>
	Task<TIntegrationEvent?> MapAsync(MappingData<TSourceEvent> mappingData, RelayContext context, EventMappingConfigurationItem? mappingConfig);
}
