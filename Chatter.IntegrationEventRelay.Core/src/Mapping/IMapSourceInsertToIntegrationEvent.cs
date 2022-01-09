using Chatter.CQRS.Events;

namespace Chatter.IntegrationEventRelay.Core.Mapping;

public interface IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent> : IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
}
