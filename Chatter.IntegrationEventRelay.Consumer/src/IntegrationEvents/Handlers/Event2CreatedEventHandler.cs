using Chatter.CQRS;
using Chatter.CQRS.Context;

namespace Chatter.IntegrationEventRelay.Consumer.IntegrationEvents.Handlers;

public class Event2CreatedEventHandler : IMessageHandler<Event2CreatedEvent>
{
    private readonly ILogger<Event2CreatedEventHandler> _logger;
    private readonly InMemoryConsumerCache _inMemoryConsumerCache;

    public Event2CreatedEventHandler(ILogger<Event2CreatedEventHandler> logger, InMemoryConsumerCache inMemoryConsumerCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inMemoryConsumerCache = inMemoryConsumerCache ?? throw new ArgumentNullException(nameof(inMemoryConsumerCache));
    }

    public Task Handle(Event2CreatedEvent message, IMessageHandlerContext context)
    {
        var messageId = context.GetInboundBrokeredMessage()?.MessageId ?? Guid.NewGuid().ToString();
        _inMemoryConsumerCache.Add(messageId, message);
        _logger.LogInformation($"{nameof(Event2CreatedEvent)} with id '{message.Id}' handled by consumer. Message Id: '{messageId}'");
        _logger.LogInformation($"{_inMemoryConsumerCache.Count} message consumed");
        return Task.CompletedTask;
    }
}
