using Chatter.CQRS.Events;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent> : IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent?>
	where TSourceEvent : class
	where TIntegrationEvent : IEvent?
{
}
