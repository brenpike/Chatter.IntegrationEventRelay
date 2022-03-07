using Chatter.IntegrationEventRelay.Core;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.SourceEvents;

public class Event1ChangedEvent : ISourceEvent
{
    public Guid Id { get; set; }
    public string? StringData { get; set; }
    public bool BoolData { get; set; }
    public int IntData { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
}
