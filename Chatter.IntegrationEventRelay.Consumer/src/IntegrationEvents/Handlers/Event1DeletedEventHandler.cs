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
        _logger.LogInformation($"{nameof(Event1DeletedEvent)} with id '{message.Id}' handled by consumer. Message Id: '{messageId}'");
        _logger.LogInformation($"{_inMemoryConsumerCache.Count} message consumed");
        return Task.CompletedTask;
    }
}
