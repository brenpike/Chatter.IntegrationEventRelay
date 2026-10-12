using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Processing;

public class MappingInvoker<TSource, TIntegrationEvent> : IMappingInvoker<TSource>
	where TSource : class
	where TIntegrationEvent : class, IEvent
{
	private readonly IServiceProvider _serviceProvider;
	private readonly IIntegrationEventPublisher _publisher;
	private readonly ILogger<MappingInvoker<TSource, TIntegrationEvent>> _logger;

	public MappingInvoker(IServiceProvider serviceProvider, IIntegrationEventPublisher publisher, ILogger<MappingInvoker<TSource, TIntegrationEvent>> logger)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Type IntegrationEventType => typeof(TIntegrationEvent);

	public async Task InvokeAsync(RowChange<TSource> change, EventMappingConfigurationItem mapping, CancellationToken cancellationToken)
	{
		_ = change ?? throw new ArgumentNullException(nameof(change));
		_ = mapping ?? throw new ArgumentNullException(nameof(mapping));

		var mapper = GetMapper(change.Operation);
		TIntegrationEvent? integrationEvent;

		try
		{
			integrationEvent = await mapper.MapAsync(new MappingData<TSource>(change.OldValue, change.NewValue), RelayContext.From(change, cancellationToken), mapping).ConfigureAwait(false);
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Error mapping '{SourceEventTypeName}' to '{IntegrationEventTypeName}'", typeof(TSource).Name, typeof(TIntegrationEvent).Name);
			throw;
		}

		if (integrationEvent is null)
		{
			_logger.LogInformation("Mapping '{SourceEventTypeName}' to '{IntegrationEventTypeName}' produced no integration event. Skipping publish.", typeof(TSource).Name, typeof(TIntegrationEvent).Name);
			return;
		}

		_logger.LogInformation("Mapped '{SourceEventTypeName}' to '{IntegrationEventTypeName}'", typeof(TSource).Name, typeof(TIntegrationEvent).Name);
		await _publisher.PublishAsync(integrationEvent, mapping, GetMessageId(change), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// A message id that stays the same when a change is replayed, so the broker can drop the duplicate. Null when the source has no positions.
	/// </summary>
	/// <remarks>
	/// Positions are only unique within a source, so the id is namespaced by source name. Every mapping of one change
	/// produces a different event type, so the event type keeps their ids apart.
	/// </remarks>
	public static string? GetMessageId(RowChange<TSource> change)
		=> change.Position is null ? null : $"{change.SourceName}:{change.Position.ToMessageId()}:{typeof(TIntegrationEvent).Name}";

	private IMapSourceToIntegrationEvent<TSource, TIntegrationEvent?> GetMapper(RowOperation operation) => operation switch
	{
		RowOperation.Insert => _serviceProvider.GetRequiredService<IMapSourceInsertToIntegrationEvent<TSource, TIntegrationEvent>>(),
		RowOperation.Update => _serviceProvider.GetRequiredService<IMapSourceUpdateToIntegrationEvent<TSource, TIntegrationEvent>>(),
		RowOperation.Delete => _serviceProvider.GetRequiredService<IMapSourceDeleteToIntegrationEvent<TSource, TIntegrationEvent>>(),
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown row operation")
	};
}
