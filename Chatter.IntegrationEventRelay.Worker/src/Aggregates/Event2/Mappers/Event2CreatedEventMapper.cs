using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.IntegrationEvents;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.SourceEvents;
using Chatter.MessageBrokers.Context;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.Mappers;

public class Event2CreatedEventMapper : IMapSourceInsertToIntegrationEvent<Event2ChangedEvent, Event2CreatedEvent>
{
	private readonly ILogger<Event2CreatedEventMapper> _logger;

	public Event2CreatedEventMapper(ILogger<Event2CreatedEventMapper> logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task<Event2CreatedEvent?> MapAsync(MappingData<Event2ChangedEvent> data, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig)
	{
		var @event = new Event2CreatedEvent()
		{
			Id = data.NewValue.Id,
			OccurredAt = DateTime.UtcNow,
			DoubleData = data.NewValue.DoubleData,
			MoreStringData = data.NewValue.MoreStringData,
			StringData = data.NewValue.StringData
		};

		_logger.LogInformation("{SourceEventTypeName} met criteria required to emit integration event '{IntegrationEventType}'", nameof(Event2ChangedEvent), nameof(Event2CreatedEvent));

		return Task.FromResult<Event2CreatedEvent?>(@event);
	}
}
