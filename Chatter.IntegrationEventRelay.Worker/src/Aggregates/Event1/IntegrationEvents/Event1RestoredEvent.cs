using Chatter.CQRS.Events;
using Chatter.MessageBrokers;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents;

[BrokeredMessage("event-1-restored")]
public class Event1RestoredEvent : IEvent
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
}
