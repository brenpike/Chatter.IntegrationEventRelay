using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;

namespace Chatter.IntegrationEventRelay.Core.Publishing;

/// <summary>
/// Publishes integration events to messaging infrastructure.
/// </summary>
public interface IIntegrationEventPublisher
{
	/// <param name="event">The integration event to publish.</param>
	/// <param name="mapping">The mapping that produced the event; supplies the destination, content type and infrastructure.</param>
	/// <param name="messageId">Optional. A stable message id so the broker can drop duplicates of a replayed change. A new id is generated if null.</param>
	Task PublishAsync<TEvent>(TEvent @event, EventMappingConfigurationItem mapping, string? messageId, CancellationToken cancellationToken)
		where TEvent : class, IEvent;
}
