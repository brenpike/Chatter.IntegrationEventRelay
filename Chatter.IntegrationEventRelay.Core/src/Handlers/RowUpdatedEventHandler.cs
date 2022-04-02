using Chatter.CQRS;
using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Handlers;

public class RowUpdatedEventHandler<TSourceEvent, TIntegrationEvent> : IMessageHandler<RowUpdatedEvent<TSourceEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly ILogger<RowUpdatedEventHandler<TSourceEvent, TIntegrationEvent>> _logger;
    private readonly IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent> _integrationEventMapper;
    private readonly IEventMappingConfigItemProvider _eventMappingConfigProvider;
    private readonly IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> _handlerExecutor;

    public RowUpdatedEventHandler(ILogger<RowUpdatedEventHandler<TSourceEvent, TIntegrationEvent>> logger,
                                  IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent> integrationEventMapper,
                                  IEventMappingConfigItemProvider eventMappingConfigProvider,
                                  IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> handlerExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _integrationEventMapper = integrationEventMapper ?? throw new ArgumentNullException(nameof(integrationEventMapper));
        _eventMappingConfigProvider = eventMappingConfigProvider ?? throw new ArgumentNullException(nameof(eventMappingConfigProvider));
        _handlerExecutor = handlerExecutor ?? throw new ArgumentNullException(nameof(handlerExecutor));
    }

    public Task Handle(RowUpdatedEvent<TSourceEvent> message, IMessageHandlerContext context)
    {
		_logger.LogInformation("Handling '{RowChangedEventTypeName}' for source changed event '{SourceEventTypeName}'", nameof(RowUpdatedEvent<TSourceEvent>), typeof(TSourceEvent).Name);
		var mappingConfig = _eventMappingConfigProvider.Get<TSourceEvent, TIntegrationEvent>(ChangeTypes.Update);
        var mapping = new MappingData<TSourceEvent>(message.OldValue, message.NewValue);
        return _handlerExecutor.Execute(_integrationEventMapper, mapping, mappingConfig, context);
    }
}
