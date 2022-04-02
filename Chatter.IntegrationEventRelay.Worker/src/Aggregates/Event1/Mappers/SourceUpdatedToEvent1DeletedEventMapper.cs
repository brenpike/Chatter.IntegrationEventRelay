using Chatter.CQRS.Context;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.SourceEvents;
using Chatter.MessageBrokers.Context;

namespace Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.Mappers;

public class SourceUpdatedToEvent1DeletedEventMapper : IMapSourceUpdateToIntegrationEvent<Event1ChangedEvent, Event1DeletedEvent>
{
    private readonly ILogger<SourceUpdatedToEvent1DeletedEventMapper> _logger;

    public SourceUpdatedToEvent1DeletedEventMapper(ILogger<SourceUpdatedToEvent1DeletedEventMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Event1DeletedEvent?> MapAsync(MappingData<Event1ChangedEvent> data, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig)
    {
        if (data.OldValue?.DeletedBy is null && data.NewValue?.DeletedBy is not null)
        {
            var @event = new Event1DeletedEvent()
            {
                Id = data.NewValue.Id,
                OccurredAt = DateTime.UtcNow,
                DeletedAt = data.NewValue.DeletedAt,
                DeletedBy = data.NewValue.DeletedBy
            };

            _logger.LogInformation("{SourceEventTypeName} met criteria required to emit integration event '{IntegrationEventTypeName}'", nameof(Event1ChangedEvent), nameof(Event1DeletedEvent));

            return Task.FromResult<Event1DeletedEvent?>(@event);
        }

        _logger.LogInformation("{SourceEventTypeName} did not meet criteria required to emit integration event '{IntegrationEventTypeName}'", nameof(Event1ChangedEvent), nameof(Event1DeletedEvent));

        return Task.FromResult<Event1DeletedEvent?>(null);
    }
}
