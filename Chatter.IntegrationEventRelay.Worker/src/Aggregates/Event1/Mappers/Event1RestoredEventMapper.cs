using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.SourceEvents;
using Chatter.MessageBrokers.Context;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.Mappers;

public class Event1RestoredEventMapper : IMapSourceUpdateToIntegrationEvent<Event1ChangedEvent, Event1RestoredEvent?>
{
	private readonly ILogger<Event1RestoredEventMapper> _logger;

	public Event1RestoredEventMapper(ILogger<Event1RestoredEventMapper> logger)
	{
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task<Event1RestoredEvent?> MapAsync(MappingData<Event1ChangedEvent> data, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig)
	{
		if (data.OldValue?.DeletedBy is not null && data.NewValue?.DeletedBy is null)
		{
			var @event = new Event1RestoredEvent()
			{
				Id = data.NewValue?.Id ?? Guid.NewGuid(),
				OccurredAt = DateTime.UtcNow
			};

			_logger.LogInformation("{SourceEventTypeName} met criteria required to emit integration event '{IntegrationEventType}'", nameof(Event1ChangedEvent), nameof(Event1RestoredEvent));

			return Task.FromResult<Event1RestoredEvent?>(@event);
		}

		_logger.LogInformation("{SourceEventTypeName} did not meet criteria required to emit integration event '{IntegrationEventType}'", nameof(Event1ChangedEvent), nameof(Event1RestoredEvent));

		return Task.FromResult<Event1RestoredEvent?>(null);
	}
}
