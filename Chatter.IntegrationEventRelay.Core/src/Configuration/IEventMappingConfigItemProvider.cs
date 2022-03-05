using Chatter.CQRS.Events;
using Chatter.SqlChangeFeed;

namespace Chatter.IntegrationEventRelay.Core.Configuration;

public interface IEventMappingConfigItemProvider
{
    public EventMappingConfigurationItem? Get<TSourceEvent, TIntegrationEvent>(ChangeTypes sourceChangeType) where TSourceEvent : class, ISourceEvent
                                                                                                        where TIntegrationEvent : class, IEvent;
}
