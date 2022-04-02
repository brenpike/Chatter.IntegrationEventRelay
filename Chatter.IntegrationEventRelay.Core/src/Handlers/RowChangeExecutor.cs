using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.MessageBrokers.Context;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Handlers;

public class RowChangeExecutor<TSourceEvent, TIntegrationEvent> : IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly ILogger<RowChangeExecutor<TSourceEvent, TIntegrationEvent>> _logger;
    private readonly IRelayIntegrationEvent _relayIntegrationEvent;

    public RowChangeExecutor(ILogger<RowChangeExecutor<TSourceEvent, TIntegrationEvent>> logger,
                             IRelayIntegrationEvent relayIntegrationEvent)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _relayIntegrationEvent = relayIntegrationEvent ?? throw new ArgumentNullException(nameof(relayIntegrationEvent));
    }

    public async Task Execute(IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent?> integrationEventMapper, MappingData<TSourceEvent> mapping, EventMappingConfigurationItem? eventMappingConfiguration, IMessageHandlerContext context)
    {
        _ = integrationEventMapper ?? throw new ArgumentNullException(nameof(integrationEventMapper), "A source to integration event mapper is required");
        _ = mapping ?? throw new ArgumentNullException(nameof(mapping), "Mapping data is required");
        _ = eventMappingConfiguration ?? throw new ArgumentNullException(nameof(eventMappingConfiguration), "An event mapping configuration item is required");
        _ = context ?? throw new ArgumentNullException(nameof(context), "Message handler context is required");

        try
        {
            TIntegrationEvent? integrationEvent;

            try
            {
                integrationEvent = await integrationEventMapper.MapAsync(mapping, (IMessageBrokerContext)context, eventMappingConfiguration);
                _logger.LogInformation("Successfully mapped '{SourceEventTypeName}' to '{IntegrationEventTypeName}'", typeof(TSourceEvent).Name, typeof(TIntegrationEvent).Name);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error mapping '{SourceEventTypeName}' to '{IntegrationEventTypeName}'", typeof(TSourceEvent).Name, typeof(TIntegrationEvent).Name);
                throw;
            }

            await _relayIntegrationEvent.RelayAsync(integrationEvent, (IMessageBrokerContext)context, eventMappingConfiguration);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error relaying '{SourceEventTypeName}' to '{IntegrationEventTypeName}'", typeof(TSourceEvent).Name, typeof(TIntegrationEvent).Name);
            throw;
        }
    }
}
