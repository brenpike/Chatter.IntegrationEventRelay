using Chatter.CQRS;
using Chatter.CQRS.Context;

namespace Chatter.IntegrationEventRelay.Consumer.IntegrationEvents.Handlers;

public class Event1DeletedEventHandler : IMessageHandler<Event1DeletedEvent>
{
    private readonly ILogger<Event1DeletedEventHandler> _logger;
    private readonly InMemoryConsumerCache _inMemoryConsumerCache;

    public Event1DeletedEventHandler(ILogger<Event1DeletedEventHandler> logger, InMemoryConsumerCache inMemoryConsumerCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inMemoryConsumerCache = inMemoryConsumerCache ?? throw new ArgumentNullException(nameof(inMemoryConsumerCache));
    }

    public Task Handle(Event1DeletedEvent message, IMessageHandlerContext context)
    {
        var messageId = context.GetInboundBrokeredMessage()?.MessageId ?? Guid.NewGuid().ToString();
        _inMemoryConsumerCache.Add(messageId, message);
        _logger.LogInformation("{EventTypeName} with id '{SourceEventId}' handled by consumer. Message Id: '{MessageId}'", nameof(Event1DeletedEvent), message.Id, messageId);
        _logger.LogInformation("{MessageConsumedCount} message consumed", _inMemoryConsumerCache.Count);
        return Task.CompletedTask;
    }
}
