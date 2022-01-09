using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.SourceEvents;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.Mappers;

public class Event1RestoredEventMapper : IMapSourceUpdateToIntegrationEvent<Event1ChangedEvent, Event1RestoredEvent>
{
    private readonly ILogger<Event1RestoredEventMapper> _logger;

    public Event1RestoredEventMapper(ILogger<Event1RestoredEventMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Event1RestoredEvent> MapAsync(MappingData<Event1ChangedEvent> data)
    {
        if (data.OldValue?.DeletedBy is not null && data.NewValue?.DeletedBy is null)
        {
            var @event = new Event1RestoredEvent()
            {
                Id = data.NewValue.Id,
                OccurredAt = DateTime.UtcNow
            };

            _logger.LogInformation($"{nameof(Event1ChangedEvent)} met criteria required to emit inetegration event '{nameof(Event1RestoredEvent)}'");

            return Task.FromResult(@event);
        }

        _logger.LogInformation($"{nameof(Event1ChangedEvent)} did not meet criteria required to emit inetegration event '{nameof(Event1RestoredEvent)}'");

        return Task.FromResult<Event1RestoredEvent>(null);
    }
}
