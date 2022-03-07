using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.IntegrationEvents;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.SourceEvents;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.Mappers;

public class Event2CreatedEventMapper : IMapSourceInsertToIntegrationEvent<Event2ChangedEvent, Event2CreatedEvent>
{
	private readonly ILogger<Event2CreatedEventMapper> _logger;

	public Event2CreatedEventMapper(ILogger<Event2CreatedEventMapper> logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task<Event2CreatedEvent?> MapAsync(MappingData<Event2ChangedEvent> data, EventMappingConfigurationItem? mappingConfig)
	{
		var @event = new Event2CreatedEvent()
		{
			Id = data.NewValue.Id,
			OccurredAt = DateTime.UtcNow,
			DoubleData = data.NewValue.DoubleData,
			MoreStringData = data.NewValue.MoreStringData,
			StringData = data.NewValue.StringData
		};

		return Task.FromResult<Event2CreatedEvent?>(@event);
	}
}
