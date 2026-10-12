using Chatter.CQRS;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Builder;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.ServiceBroker.Handlers;
using Chatter.SqlChangeFeed;
using Chatter.SqlChangeFeed.Configuration;
using Chatter.SqlChangeFeed.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Extensions;

public static class IntegrationEventRelayBuilderExtensions
{
	/// <summary>
	/// Captures changes for every mapping whose source is of type "ServiceBroker", including mappings that don't name
	/// a source. Each source event type gets a change feed (trigger and SQL Service Broker queue) on its table.
	/// </summary>
	public static IIntegrationEventRelayBuilder UseServiceBrokerChangeSource(this IIntegrationEventRelayBuilder builder, Action<SqlChangeFeedOptionsBuilder>? optionsBuilder = null)
	{
		var configuration = builder.Configuration;
		var mappings = configuration.GetMappingsForSourceType(ServiceBrokerChangeSource.SourceType).ToList();

		var sourceNames = mappings.Select(EventMappingConfiguration.GetSourceName)
			.Concat(configuration.Sources.Where(s => string.Equals(s.Value.Type, ServiceBrokerChangeSource.SourceType, StringComparison.OrdinalIgnoreCase)).Select(s => s.Key))
			.Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var sourceName in sourceNames)
			builder.Services.AddSingleton<IChangeSource>(new ServiceBrokerChangeSource(sourceName));

		foreach (var mappingsForType in mappings.GroupBy(m => m.SourceEventType))
		{
			var sourceType = mappingsForType.Key ?? throw new ArgumentNullException(nameof(EventMappingConfigurationItem.SourceEventType), "A source event type is required.");
			if (!typeof(IMessage).IsAssignableFrom(sourceType))
				throw new ArgumentException($"Source event type '{sourceType.FullName}' is captured by SQL Service Broker, so it must implement {typeof(IMessage).FullName} (for example through ISourceEvent).");

			var feeds = mappingsForType.Select(m => GetFeed(configuration, m)).Distinct().ToList();
			if (feeds.Count > 1)
				throw new ArgumentException($"Source event type '{sourceType.FullName}' is mapped to more than one SQL Service Broker table or source ({string.Join("; ", feeds.Select(f => $"source '{f.SourceName}', database '{f.DatabaseName}', table '{f.TableName}'"))}). " +
					"SqlChangeFeed creates one queue per source event type, so use a separate source event type for each table.");

			var feed = feeds[0];
			var bindingType = typeof(ServiceBrokerBinding<>).MakeGenericType(sourceType);
			builder.Services.AddSingleton(bindingType, Activator.CreateInstance(bindingType, feed.SourceName, new SourceTable(feed.TableName, Database: feed.DatabaseName))!);

			foreach (var operation in mappingsForType.Select(m => m.SourceChangeType).Distinct())
			{
				var (rowEvent, handler) = operation switch
				{
					RowOperation.Insert => (typeof(RowInsertedEvent<>), typeof(RowInsertedEventHandler<>)),
					RowOperation.Update => (typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<>)),
					RowOperation.Delete => (typeof(RowDeletedEvent<>), typeof(RowDeletedEventHandler<>)),
					_ => throw new ArgumentOutOfRangeException(nameof(EventMappingConfigurationItem.SourceChangeType), operation, "Unknown row operation")
				};
				builder.Services.AddTransient(typeof(IMessageHandler<>).MakeGenericType(rowEvent.MakeGenericType(sourceType)), handler.MakeGenericType(sourceType));
			}

			builder.ChatterBuilder.AddSqlChangeFeed(sourceType, feed.ConnectionString, feed.DatabaseName, feed.TableName, optionsBuilder);
			builder.Services.AddSingleton(new ServiceBrokerFeed(sourceType));
		}

		return builder;
	}

	private static FeedTarget GetFeed(EventMappingConfiguration configuration, EventMappingConfigurationItem mapping)
	{
		var connectionString = configuration.GetConnectionString(mapping);
		if (string.IsNullOrWhiteSpace(connectionString))
			throw new ArgumentNullException(nameof(connectionString), $"A connection string is required to watch for change events on table '{mapping.TableName}'. " +
				$"Set 'ConnectionString' on {nameof(EventMappingConfigurationItem)}, its source or {nameof(EventMappingConfiguration)}");

		var databaseName = string.IsNullOrWhiteSpace(mapping.DatabaseName) ? new SqlConnectionStringBuilder(connectionString).InitialCatalog : mapping.DatabaseName;
		if (string.IsNullOrWhiteSpace(databaseName))
			throw new ArgumentNullException(nameof(databaseName), $"A database is required to watch for change events on table '{mapping.TableName}'. " +
				$"Set Database/InitialCatalog of connection string or {nameof(EventMappingConfigurationItem)}.DatabaseName");

		if (string.IsNullOrWhiteSpace(mapping.TableName))
			throw new ArgumentNullException(nameof(mapping.TableName), $"A table is required to watch for change events for source event type '{mapping.SourceEventTypeName}'.");

		return new FeedTarget(EventMappingConfiguration.GetSourceName(mapping), connectionString, databaseName, mapping.TableName);
	}

	private sealed record FeedTarget(string SourceName, string ConnectionString, string DatabaseName, string TableName)
	{
		public bool Equals(FeedTarget? other)
			=> other is not null
				&& string.Equals(SourceName, other.SourceName, StringComparison.OrdinalIgnoreCase)
				&& ConnectionString == other.ConnectionString
				&& string.Equals(DatabaseName, other.DatabaseName, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(TableName, other.TableName, StringComparison.OrdinalIgnoreCase);

		public override int GetHashCode()
			=> HashCode.Combine(SourceName.ToUpperInvariant(), ConnectionString, DatabaseName.ToUpperInvariant(), TableName.ToUpperInvariant());
	}
}
