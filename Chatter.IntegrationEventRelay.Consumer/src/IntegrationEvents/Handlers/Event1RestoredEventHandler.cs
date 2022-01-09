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
        _logger.LogInformation($"{nameof(Event1RestoredEvent)} with id '{message.Id}' handled by consumer. Message Id: '{messageId}'");
        _logger.LogInformation($"{_inMemoryConsumerCache.Count} message consumed");
        return Task.CompletedTask;
    }
}
