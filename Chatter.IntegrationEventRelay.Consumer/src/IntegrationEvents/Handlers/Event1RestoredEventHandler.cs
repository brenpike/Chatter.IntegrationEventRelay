using Chatter.CQRS;
using Chatter.CQRS.Context;

namespace Chatter.IntegrationEventRelay.Consumer.IntegrationEvents.Handlers;

public class Event1RestoredEventHandler : IMessageHandler<Event1RestoredEvent>
{
    private readonly ILogger<Event1RestoredEventHandler> _logger;
    private readonly InMemoryConsumerCache _inMemoryConsumerCache;

    public Event1RestoredEventHandler(ILogger<Event1RestoredEventHandler> logger, InMemoryConsumerCache inMemoryConsumerCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inMemoryConsumerCache = inMemoryConsumerCache ?? throw new ArgumentNullException(nameof(inMemoryConsumerCache));
    }

    public Task Handle(Event1RestoredEvent message, IMessageHandlerContext context)
    {
        var messageId = context.GetInboundBrokeredMessage()?.MessageId ?? Guid.NewGuid().ToString();
        _inMemoryConsumerCache.Add(messageId, message);
		_logger.LogInformation("{EventTypeName} with id '{SourceEventId}' handled by consumer. Message Id: '{MessageId}'", nameof(Event1RestoredEvent), message.Id, messageId);
		_logger.LogInformation("{MessageConsumedCount} message consumed", _inMemoryConsumerCache.Count);
		return Task.CompletedTask;
    }
}
