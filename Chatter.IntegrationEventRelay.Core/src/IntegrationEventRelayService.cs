using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.MessageBrokers.Context;
using Chatter.MessageBrokers.Routing.Options;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core;

public class IntegrationEventRelayService : IRelayIntegrationEvent
{
	private readonly ILogger<IntegrationEventRelayService> _logger;

	public IntegrationEventRelayService(ILogger<IntegrationEventRelayService> logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	public async Task RelayAsync<TIntegrationEvent>(TIntegrationEvent? @event, IMessageBrokerContext context, EventMappingConfigurationItem settings) where TIntegrationEvent : class, IEvent
	{
		_logger.LogInformation($"Relaying integration event '{typeof(TIntegrationEvent).Name}' to messaging infrastructure");

		if (settings == null)
		{
			_logger.LogWarning($"Unable to relay integration event '{typeof(TIntegrationEvent).Name}'. The supplied {nameof(EventMappingConfigurationItem)} was null.");
			return;
		}

		if (@event != null)
		{
			try
			{
				var publishOptions = new PublishOptions
				{
					ContentType = settings.BrokeredMessageContentType
				};
				publishOptions.UseMessagingInfrastructure(i => settings.MessagingInfrastructureType);
				_logger.LogTrace("Attempting to publish integration event to messaging infrastructure");
				_logger.LogDebug("Messaging Infrastructure Type: '{MessagingInfrastructureType}', Infrastructure Message Name: '{InfrastructureMessageName}'", settings.MessagingInfrastructureType, settings.InfrastructureMessageName);
				await context.Publish(@event, settings.InfrastructureMessageName, publishOptions);
				_logger.LogInformation("Successfully published integration event to messaging infrastructure");
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Error publishing integration event to messaging infrastructure");
				throw;
			}
		}
		else
		{
			_logger.LogInformation("Integration event '{IntegrationEventTypeName}' is null. Skipping publish to messaging infrastructure.", typeof(TIntegrationEvent).Name);
		}
	}
}
