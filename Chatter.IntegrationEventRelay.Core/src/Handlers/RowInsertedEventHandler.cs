using Chatter.CQRS;
using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Handlers;

public class RowInsertedEventHandler<TSourceEvent, TIntegrationEvent> : IMessageHandler<RowInsertedEvent<TSourceEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly ILogger<RowInsertedEventHandler<TSourceEvent, TIntegrationEvent>> _logger;
    private readonly IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent> _integrationEventMapper;
    private readonly IEventMappingConfigItemProvider _eventMappingConfigProvider;
    private readonly IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> _handlerExecutor;

    public RowInsertedEventHandler(ILogger<RowInsertedEventHandler<TSourceEvent, TIntegrationEvent>> logger,
                                  IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent> integrationEventMapper,
                                  IEventMappingConfigItemProvider eventMappingConfigProvider,
                                  IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> handlerExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _integrationEventMapper = integrationEventMapper ?? throw new ArgumentNullException(nameof(integrationEventMapper));
        _eventMappingConfigProvider = eventMappingConfigProvider ?? throw new ArgumentNullException(nameof(eventMappingConfigProvider));
        _handlerExecutor = handlerExecutor ?? throw new ArgumentNullException(nameof(handlerExecutor));
    }

    public Task Handle(RowInsertedEvent<TSourceEvent> message, IMessageHandlerContext context)
    {
		_logger.LogInformation("Handling '{RowChangedEventTypeName}' for source changed event '{SourceEventTypeName}'", nameof(RowInsertedEvent<TSourceEvent>), typeof(TSourceEvent).Name);
		var mappingConfig = _eventMappingConfigProvider.Get<TSourceEvent, TIntegrationEvent>(ChangeTypes.Insert);
        var mapping = new MappingData<TSourceEvent>(message.Inserted);
        return _handlerExecutor.Execute(_integrationEventMapper, mapping, mappingConfig, context);
    }
}
