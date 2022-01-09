using Chatter.CQRS.Events;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent> : IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
}
