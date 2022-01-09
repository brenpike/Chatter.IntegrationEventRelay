using Chatter.CQRS.Events;
using Chatter.MessageBrokers;

namespace Chatter.IntegrationEventRelay.Consumer.IntegrationEvents;

[BrokeredMessage("event-1-deleted", "sub1")]
public class Event1DeletedEvent : IEvent
{
    public Guid Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
    public DateTime OccurredAt { get; set; }
}
