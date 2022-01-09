using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.SourceEvents;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.Mappers;

public class SourceDeletedToEvent1DeletedEventMapper : IMapSourceDeleteToIntegrationEvent<Event1ChangedEvent, Event1DeletedEvent>
{
    private readonly ILogger<SourceDeletedToEvent1DeletedEventMapper> _logger;

    public SourceDeletedToEvent1DeletedEventMapper(ILogger<SourceDeletedToEvent1DeletedEventMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Event1DeletedEvent> MapAsync(MappingData<Event1ChangedEvent> data)
    {
        _logger.LogInformation($"Mapping {nameof(Event1ChangedEvent)} to {nameof(Event1DeletedEvent)}");

        if (data.NewValue?.DeletedBy is null)
        {
            var @event = new Event1DeletedEvent()
            {
                Id = data.NewValue.Id,
                OccurredAt = DateTime.UtcNow,
                DeletedAt = data.NewValue.DeletedAt,
                DeletedBy = data.NewValue.DeletedBy
            };

            return Task.FromResult(@event);
        }

        _logger.LogInformation($"{nameof(Event1ChangedEvent)} was already soft-deleted. Skipping emitting of integration event '{nameof(Event1DeletedEvent)}'");

        return Task.FromResult<Event1DeletedEvent>(null);
    }
}
