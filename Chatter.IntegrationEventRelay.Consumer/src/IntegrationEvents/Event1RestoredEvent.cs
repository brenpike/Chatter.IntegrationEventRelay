using Chatter.CQRS.Events;
using Chatter.MessageBrokers;

namespace Chatter.IntegrationEventRelay.Consumer.IntegrationEvents;

[BrokeredMessage("event-1-restored", "sub1")]
public class Event1RestoredEvent : IEvent
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
}
