using Chatter.CQRS.Events;
using Chatter.SqlTableWatcher;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Configuration;

public class EventMappingConfigItemProvider : IEventMappingConfigItemProvider
{
    private readonly EventMappingConfiguration _integrationEventSourceConfig;
    private readonly ILogger<EventMappingConfigItemProvider> _logger;

    public EventMappingConfigItemProvider(EventMappingConfiguration integrationEventSourceConfig, ILogger<EventMappingConfigItemProvider> logger)
    {
        _integrationEventSourceConfig = integrationEventSourceConfig ?? throw new ArgumentNullException(nameof(integrationEventSourceConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public EventMappingConfigurationItem Get<TSourceEvent, TIntegrationEvent>(ChangeTypes sourceChangeType) where TSourceEvent : class, ISourceEvent
                                                                                                        where TIntegrationEvent : class, IEvent
    {
        try
        {
            var mappings = _integrationEventSourceConfig.Mappings
                .Where(m =>
                    m.SourceEventType == typeof(TSourceEvent)
                    && m.IntegrationEventType == typeof(TIntegrationEvent)
                    && m.SourceChangeType == sourceChangeType);

            if (!mappings.Any())
            {
                _logger.LogDebug($"No mapping config found for source event type '{typeof(TSourceEvent).Name}' and integration event type '{typeof(TIntegrationEvent).Name}'");
                return null;
            }

            if (mappings.Count() > 1)
            {
                _logger.LogWarning($"More than one mapping config found for source event type '{typeof(TSourceEvent).Name}' and integration event type '{typeof(TIntegrationEvent).Name}'. Returning the first mapping found.");
            }

            return mappings.First();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, $"Unable to get mapping config.");
            return null;
        }
    }
}
