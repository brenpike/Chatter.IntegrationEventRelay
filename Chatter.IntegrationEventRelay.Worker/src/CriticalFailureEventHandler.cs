using Chatter.CQRS;
using Chatter.CQRS.Context;
using Chatter.MessageBrokers.Recovery;

namespace Chatter.IntegrationEventRelay.Worker
{
    public class CriticalFailureEventHandler : IMessageHandler<CriticalFailureEvent>
    {
        private readonly ILogger<CriticalFailureEventHandler> _logger;

        public CriticalFailureEventHandler(ILogger<CriticalFailureEventHandler> logger)
            => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public Task Handle(CriticalFailureEvent message, IMessageHandlerContext context)
        {
            //send message to email queue to notify?
            _logger.LogCritical($"{nameof(CriticalFailureEvent)} received. {message.Context}");
            return Task.CompletedTask;
        }
    }
}
