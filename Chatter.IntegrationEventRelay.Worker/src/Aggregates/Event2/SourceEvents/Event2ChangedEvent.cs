using Chatter.IntegrationEventRelay.Core;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.SourceEvents;

public class Event2ChangedEvent : ISourceEvent
{
	public Guid Id { get; set; }
	public string? StringData { get; set; }
	public string? MoreStringData { get; set; }
	public double DoubleData { get; set; }
}
