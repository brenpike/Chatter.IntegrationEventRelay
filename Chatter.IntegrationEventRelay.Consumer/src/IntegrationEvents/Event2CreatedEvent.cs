using Chatter.CQRS.Events;
using Chatter.MessageBrokers;

namespace Chatter.IntegrationEventRelay.Consumer.IntegrationEvents;

[BrokeredMessage("event-2-created", "sub1")]
public class Event2CreatedEvent : IEvent
{
    public Guid Id { get; set; }
    public string StringData { get; set; }
    public string MoreStringData { get; set; }
    public double DoubleData { get; set; }
    public DateTime OccurredAt { get; set; }
}
