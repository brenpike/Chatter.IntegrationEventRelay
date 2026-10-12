using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Builder;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chatter.IntegrationEventRelay.Cdc.Extensions;

public static class IntegrationEventRelayBuilderExtensions
{
	/// <summary>
	/// The mapping setting naming the capture instance to read. Default <c>&lt;TableSchema&gt;_&lt;TableName&gt;</c>, SQL Server's default name.
	/// </summary>
	public const string CaptureInstanceKey = "CaptureInstance";

	/// <summary>
	/// The mapping setting naming the schema of the mapping's table. Default <c>dbo</c>.
	/// </summary>
	public const string TableSchemaKey = "TableSchema";

	private const string DefaultTableSchema = "dbo";

	/// <summary>
	/// Captures changes with SQL Server Change Data Capture for every mapping whose source is of type "Cdc". Each capture
	/// instance gets a poll loop; its checkpoints are stored in the source database, in the relay's
	/// <see cref="EventMappingConfiguration.Schema"/>.
	/// </summary>
	/// <param name="configure">Optional. Adjusts the options of every CDC source after they're read from configuration.</param>
	/// <remarks>
	/// Mappings can set <c>CaptureInstance</c> and <c>TableSchema</c>. Source event types need a public parameterless
	/// constructor; captured columns set the properties of the same name. Integration events are published outside any
	/// receive transaction, so Chatter's outbox isn't supported.
	/// </remarks>
	public static IIntegrationEventRelayBuilder UseCdcChangeSource(this IIntegrationEventRelayBuilder builder, Action<CdcSourceOptions>? configure = null)
	{
		var configuration = builder.Configuration;
		var mappings = configuration.GetMappingsForSourceType(CdcChangeSource.SourceType).ToList();
		if (mappings.Count > 0)
			_ = SqlIdentifier.Quote(configuration.Schema ?? string.Empty); // fail now, not when the first poller starts

		var sourceNames = mappings.Select(EventMappingConfiguration.GetSourceName)
			.Concat(configuration.Sources.Where(s => string.Equals(s.Value.Type, CdcChangeSource.SourceType, StringComparison.OrdinalIgnoreCase)).Select(s => s.Key))
			.Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var sourceName in sourceNames)
			builder.Services.AddSingleton<IChangeSource>(new CdcChangeSource(sourceName));

		builder.Services.TryAddSingleton(TimeProvider.System);
		builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, CdcPollerService>());

		foreach (var target in mappings.Select(m => GetTarget(configuration, m)).GroupBy(t => (t.SourceName.ToUpperInvariant(), t.CaptureInstance.ToUpperInvariant())))
		{
			var first = target.First();
			var conflicts = target.Select(t => (t.SourceEventType, t.ConnectionString, Table: t.Table.ToString().ToUpperInvariant())).Distinct().ToList();
			if (conflicts.Count > 1)
				throw new ArgumentException($"Capture instance '{first.CaptureInstance}' of CDC source '{first.SourceName}' is mapped with more than one source event type, table or connection string " +
					$"({string.Join("; ", target.Select(t => $"'{t.SourceEventType.FullName}' from table '{t.Table}'"))}). Map each capture instance to one source event type.");

			var sourceEventType = first.SourceEventType;
			if (sourceEventType.IsAbstract || sourceEventType.GetConstructor(Type.EmptyTypes) is null)
				throw new ArgumentException($"Source event type '{sourceEventType.FullName}' is captured by CDC, so it must be a concrete class with a public parameterless constructor.");

			var options = GetOptions(configuration, first.SourceName, configure);
			var capture = new CdcCapture(first.SourceName, first.CaptureInstance, first.Table, sourceEventType, options);
			var connectionString = first.ConnectionString;
			var schema = configuration.Schema;
			var pollerType = typeof(CdcCapturePoller<>).MakeGenericType(sourceEventType);

			builder.Services.AddSingleton(new CdcMigrationTarget(connectionString, schema, first.TableSchema, first.Table.Name, first.CaptureInstance));
			builder.Services.AddSingleton(sp => (ICdcCapturePoller)ActivatorUtilities.CreateInstance(sp, pollerType,
				capture, (ICdcQueries)new SqlCdcQueries(connectionString), (ICdcCheckpointStore)new SqlCheckpointStore(connectionString, schema), (ICdcLeaseFactory)new SqlAppLockLeaseFactory(connectionString)));
		}

		return builder;
	}

	private static CdcSourceOptions GetOptions(EventMappingConfiguration configuration, string sourceName, Action<CdcSourceOptions>? configure)
	{
		var options = new CdcSourceOptions();
		if (configuration.Sources.TryGetValue(sourceName, out var source))
			source.Section?.Bind(options);
		configure?.Invoke(options);

		var problems = options.Validate().ToList();
		if (problems.Count > 0)
			throw new ArgumentException($"CDC source '{sourceName}' is misconfigured: {string.Join(" ", problems)}");

		return options;
	}

	private static CaptureTarget GetTarget(EventMappingConfiguration configuration, EventMappingConfigurationItem mapping)
	{
		var sourceName = EventMappingConfiguration.GetSourceName(mapping);
		var sourceEventType = mapping.SourceEventType ?? throw new ArgumentNullException(nameof(EventMappingConfigurationItem.SourceEventType), "A source event type is required.");

		if (string.IsNullOrWhiteSpace(mapping.TableName))
			throw new ArgumentNullException(nameof(mapping.TableName), $"A table is required to capture changes for source event type '{mapping.SourceEventTypeName}'.");

		var connectionString = configuration.GetConnectionString(mapping);
		if (string.IsNullOrWhiteSpace(connectionString))
			throw new ArgumentNullException(nameof(connectionString), $"A connection string is required to capture changes to table '{mapping.TableName}'. " +
				$"Set 'ConnectionString' on {nameof(EventMappingConfigurationItem)}, its source or {nameof(EventMappingConfiguration)}");

		// CDC reads the database the connection string names; a mapping's DatabaseName only labels its changes
		var databaseName = string.IsNullOrWhiteSpace(mapping.DatabaseName) ? new SqlConnectionStringBuilder(connectionString).InitialCatalog : mapping.DatabaseName;
		var tableSchema = mapping.Section?[TableSchemaKey] is { Length: > 0 } schema ? schema : DefaultTableSchema;
		var captureInstance = mapping.Section?[CaptureInstanceKey] is { Length: > 0 } instance ? instance : $"{tableSchema}_{mapping.TableName}";

		return new CaptureTarget(sourceName, captureInstance, sourceEventType, connectionString, tableSchema,
			new SourceTable(mapping.TableName, tableSchema, string.IsNullOrWhiteSpace(databaseName) ? null : databaseName));
	}

	private sealed record CaptureTarget(string SourceName, string CaptureInstance, Type SourceEventType, string ConnectionString, string TableSchema, SourceTable Table);
}
