using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Processing;

public class RowChangeProcessor<TSource> : IRowChangeProcessor<TSource> where TSource : class
{
	private readonly EventMappingConfiguration _configuration;
	private readonly IEnumerable<IMappingInvoker<TSource>> _invokers;
	private readonly ILogger<RowChangeProcessor<TSource>> _logger;

	public RowChangeProcessor(EventMappingConfiguration configuration, IEnumerable<IMappingInvoker<TSource>> invokers, ILogger<RowChangeProcessor<TSource>> logger)
	{
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_invokers = invokers ?? throw new ArgumentNullException(nameof(invokers));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task ProcessAsync(RowChange<TSource> change, CancellationToken cancellationToken)
	{
		_ = change ?? throw new ArgumentNullException(nameof(change));

		var mappings = GetMappings(change).ToList();
		if (mappings.Count == 0)
		{
			_logger.LogDebug("No mapping for {Operation} of '{SourceEventTypeName}' from source '{SourceName}'", change.Operation, typeof(TSource).Name, change.SourceName);
			return;
		}

		_logger.LogInformation("Processing {Operation} of '{SourceEventTypeName}' from source '{SourceName}' with {MappingCount} mapping(s)", change.Operation, typeof(TSource).Name, change.SourceName, mappings.Count);

		foreach (var mapping in mappings)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var invoker = _invokers.FirstOrDefault(i => i.IntegrationEventType == mapping.IntegrationEventType)
				?? throw new InvalidOperationException($"No mapping invoker is registered for integration event type '{mapping.IntegrationEventType}'.");
			await invoker.InvokeAsync(change, mapping, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Mappings for this source, source type and operation; and for this table (and database, when both the source and
	/// the mapping name one) when the source reports a table.
	/// </summary>
	private IEnumerable<EventMappingConfigurationItem> GetMappings(RowChange<TSource> change)
		=> (_configuration.Mappings ?? Enumerable.Empty<EventMappingConfigurationItem>())
			.Where(m => m.SourceEventType == typeof(TSource)
				&& m.SourceChangeType == change.Operation
				&& string.Equals(EventMappingConfiguration.GetSourceName(m), change.SourceName, StringComparison.OrdinalIgnoreCase)
				&& (change.Table is null || change.Table.Matches(m.TableName, m.DatabaseName)));
}
