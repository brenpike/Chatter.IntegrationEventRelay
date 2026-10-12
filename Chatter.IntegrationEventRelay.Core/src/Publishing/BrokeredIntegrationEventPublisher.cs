using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.MessageBrokers.Context;
using Chatter.MessageBrokers.Routing.Options;
using Chatter.MessageBrokers.Sending;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Publishing;

/// <summary>
/// Publishes integration events through Chatter's message brokers, outside of any inbound message's transaction.
/// </summary>
public class BrokeredIntegrationEventPublisher : IIntegrationEventPublisher
{
	private readonly IBrokeredMessagePublisher _publisher;
	private readonly ILogger<BrokeredIntegrationEventPublisher> _logger;

	public BrokeredIntegrationEventPublisher(IBrokeredMessageDispatcher publisher, ILogger<BrokeredIntegrationEventPublisher> logger)
	{
		_publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task PublishAsync<TEvent>(TEvent @event, EventMappingConfigurationItem mapping, string? messageId, CancellationToken cancellationToken)
		where TEvent : class, IEvent
	{
		_ = @event ?? throw new ArgumentNullException(nameof(@event));
		_ = mapping ?? throw new ArgumentNullException(nameof(mapping));

		var publishOptions = new PublishOptions
		{
			ContentType = mapping.BrokeredMessageContentType,
			MessageId = messageId
		};
		publishOptions.UseMessagingInfrastructure(i => mapping.MessagingInfrastructureType);

		_logger.LogDebug("Publishing integration event '{IntegrationEventTypeName}'. Messaging Infrastructure Type: '{MessagingInfrastructureType}', Infrastructure Message Name: '{InfrastructureMessageName}', Message Id: '{MessageId}'",
			typeof(TEvent).Name, mapping.MessagingInfrastructureType, mapping.InfrastructureMessageName, messageId);

		try
		{
			await _publisher.Publish(@event, mapping.InfrastructureMessageName, transactionContext: (TransactionContext?)null, options: publishOptions).ConfigureAwait(false);
			_logger.LogInformation("Published integration event '{IntegrationEventTypeName}' to messaging infrastructure", typeof(TEvent).Name);
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Error publishing integration event '{IntegrationEventTypeName}' to messaging infrastructure", typeof(TEvent).Name);
			throw;
		}
	}
}
