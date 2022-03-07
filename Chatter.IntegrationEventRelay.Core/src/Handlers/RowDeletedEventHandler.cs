using Chatter.CQRS;
using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Handlers;

public class RowDeletedEventHandler<TSourceEvent, TIntegrationEvent> : IMessageHandler<RowDeletedEvent<TSourceEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly ILogger<RowDeletedEventHandler<TSourceEvent, TIntegrationEvent>> _logger;
    private readonly IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent?> _integrationEventMapper;
    private readonly IEventMappingConfigItemProvider _eventMappingConfigProvider;
    private readonly IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> _handlerExecutor;

    public RowDeletedEventHandler(ILogger<RowDeletedEventHandler<TSourceEvent, TIntegrationEvent>> logger,
                                  IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent?> integrationEventMapper,
                                  IEventMappingConfigItemProvider eventMappingConfigProvider,
                                  IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> handlerExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _integrationEventMapper = integrationEventMapper ?? throw new ArgumentNullException(nameof(integrationEventMapper));
        _eventMappingConfigProvider = eventMappingConfigProvider ?? throw new ArgumentNullException(nameof(eventMappingConfigProvider));
        _handlerExecutor = handlerExecutor ?? throw new ArgumentNullException(nameof(handlerExecutor));
    }

    public Task Handle(RowDeletedEvent<TSourceEvent> message, IMessageHandlerContext context)
    {
        _logger.LogInformation($"Handling '{nameof(RowDeletedEvent<TSourceEvent>)}' for source changed event '{typeof(TSourceEvent).Name}'");
        var mappingConfig = _eventMappingConfigProvider.Get<TSourceEvent, TIntegrationEvent>(ChangeTypes.Delete);
        var mapping = new MappingData<TSourceEvent>(message.Deleted);
        return _handlerExecutor.Execute(_integrationEventMapper, mapping, mappingConfig, context);
    }
}
