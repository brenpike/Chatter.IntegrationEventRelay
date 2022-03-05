using Chatter.CQRS;
using Chatter.CQRS.DependencyInjection;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.SqlChangeFeed;
using Chatter.SqlChangeFeed.Configuration;
using Chatter.SqlChangeFeed.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data.SqlClient;

namespace Chatter.IntegrationEventRelay.Core.Extensions;

public static class ChatterBuilderExtensions
{
	public static IChatterBuilder AddIntegrationEventRelay(this IChatterBuilder builder, EventMappingConfiguration eventMappingConfig, Action<SqlChangeFeedOptionsBuilder>? optionsBuilder = null)
	{
		builder.Services.AddSingleton(eventMappingConfig);
		builder.Services.AddTransient<IEventMappingConfigItemProvider, EventMappingConfigItemProvider>();
		builder.Services.AddTransient<IRelayIntegrationEvent, IntegrationEventRelayService>();

		var assemblies = builder.AssemblySourceFilter.Apply();

		foreach (var map in eventMappingConfig.Mappings ?? new List<EventMappingConfigurationItem>())
		{
			map.IntegrationEventType = map.IntegrationEventTypeName?.GetTypeFromString(assemblies);
			map.SourceEventType = map.SourceEventTypeName?.GetTypeFromString(assemblies);
			_ = map.SourceEventType ?? throw new ArgumentNullException(nameof(map.SourceEventType), $"No type found for {nameof(map.SourceEventTypeName)} for mapping to database '{map.DatabaseName}' and table '{map.TableName}'");
			_ = map.IntegrationEventType ?? throw new ArgumentNullException(nameof(map.IntegrationEventType), $"No type found for {nameof(map.IntegrationEventTypeName)} for mapping to database '{map.DatabaseName}' and table '{map.TableName}'");

			builder.WireHandlerExecutor(map);

			if (map.SourceChangeType == ChangeTypes.Update)
			{
				builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<,>), map);
				builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), map);
			}

			if (map.SourceChangeType == ChangeTypes.Delete)
			{
				builder.WireRowChangeEventHandlerFromConfig(typeof(RowDeletedEvent<>), typeof(RowDeletedEventHandler<,>), map);
				builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceDeleteToIntegrationEvent<,>), map);
			}

			if (map.SourceChangeType == ChangeTypes.Insert)
			{
				builder.WireRowChangeEventHandlerFromConfig(typeof(RowInsertedEvent<>), typeof(RowInsertedEventHandler<,>), map);
				builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceInsertToIntegrationEvent<,>), map);
			}
		}

		builder.AddSqlChangeFeedPerUniqueDataSource(eventMappingConfig, optionsBuilder);

		return builder;
	}

	public static IChatterBuilder AddIntegrationEventRelay(this IChatterBuilder builder, string sectionName = "IntegrationEventRelay", Action<SqlChangeFeedOptionsBuilder>? optionsBuilder = null)
	{
		var eventMappingConfig = new EventMappingConfiguration();
		builder.Configuration.GetSection(sectionName).Bind(eventMappingConfig);
		return builder.AddIntegrationEventRelay(eventMappingConfig, optionsBuilder);
	}

	public static IChatterBuilder AddSqlChangeFeedPerUniqueDataSource(this IChatterBuilder builder, EventMappingConfiguration relayConfiguration, Action<SqlChangeFeedOptionsBuilder>? optionsBuilder)
	{
		if (relayConfiguration.Mappings is null)
			return builder;

		var mappingGroups = relayConfiguration.Mappings.GroupBy(d => (d.ConnectionString, d.DatabaseName, d.TableName, d.SourceEventType));
		foreach (var mappingGroup in mappingGroups)
		{
			var connString = string.IsNullOrWhiteSpace(mappingGroup.Key.ConnectionString) ? relayConfiguration.ConnectionString : mappingGroup.Key.ConnectionString;
			if (string.IsNullOrWhiteSpace(connString))
				throw new ArgumentNullException(nameof(connString), $"A connection string is required to watch for change events on table '{mappingGroup.Key.TableName}'. " +
					$"Set 'ConnectionString' on {nameof(EventMappingConfigurationItem)} or {nameof(EventMappingConfiguration)}");

			var connStringBuilder = new SqlConnectionStringBuilder(connString);
			string databaseName = string.IsNullOrWhiteSpace(mappingGroup.Key.DatabaseName) ? connStringBuilder.InitialCatalog : mappingGroup.Key.DatabaseName;
			if (string.IsNullOrWhiteSpace(databaseName))
				throw new ArgumentNullException(nameof(databaseName), $"A database is required to watch for change events on table '{mappingGroup.Key.TableName}'. " +
					$"Set Database/InitialCatalog of connection string or {nameof(EventMappingConfigurationItem)}.DatabaseName");

			builder.AddSqlChangeFeed(mappingGroup.Key.SourceEventType, connString, databaseName, mappingGroup.Key.TableName, optionsBuilder);
		}

		return builder;
	}

	public static IChatterBuilder WireSourceToIntegrationEventMappers(this IChatterBuilder builder, Type openSourceToIntegrationEventMapperType, EventMappingConfigurationItem map)
	{
		_ = map ?? throw new ArgumentNullException(nameof(map), "A source to integration event mapping is required.");
		_ = builder.AssemblySourceFilter ?? throw new ArgumentNullException(nameof(builder.AssemblySourceFilter), "An assembly source builder is required.");
		_ = map.SourceEventType ?? throw new ArgumentNullException(nameof(map.SourceEventType), "A source event type is required.");
		_ = map.IntegrationEventType ?? throw new ArgumentNullException(nameof(map.IntegrationEventType), "An integration event type is required.");

		var assemblies = builder.AssemblySourceFilter.Apply();

		var closeSourceToIntegrationEventMapperType = openSourceToIntegrationEventMapperType.MakeGenericType(map.SourceEventType, map.IntegrationEventType);

		builder.Services.Scan(s =>
			s.FromAssemblies(assemblies)
				.AddClasses(c => c.AssignableTo(closeSourceToIntegrationEventMapperType))
				.UsingRegistrationStrategy(Scrutor.RegistrationStrategy.Append)
				.AsImplementedInterfaces()
				.WithTransientLifetime());

		return builder;
	}

	public static IChatterBuilder WireRowChangeEventHandlerFromConfig(this IChatterBuilder builder, Type openGenericConcreteRowChangeEventType, Type openGenericConcreteRowChangeEventHandlerType, EventMappingConfigurationItem map)
	{
		_ = map ?? throw new ArgumentNullException(nameof(map), "Event mapping configuration cannot be null");
		_ = openGenericConcreteRowChangeEventType ?? throw new ArgumentNullException(nameof(openGenericConcreteRowChangeEventType), "A source event type is required.");
		_ = openGenericConcreteRowChangeEventHandlerType ?? throw new ArgumentNullException(nameof(openGenericConcreteRowChangeEventHandlerType), "An integration event type is required.");
		_ = map.SourceEventType ?? throw new ArgumentNullException(nameof(map.SourceEventType), "A source event type is required.");
		_ = map.IntegrationEventType ?? throw new ArgumentNullException(nameof(map.IntegrationEventType), "An integration event type is required.");

		if (!openGenericConcreteRowChangeEventType.IsGenericTypeDefinition)
			throw new ArgumentException(nameof(openGenericConcreteRowChangeEventType), "An open generic type definition is required.");

		if (!openGenericConcreteRowChangeEventHandlerType.IsGenericTypeDefinition)
			throw new ArgumentException(nameof(openGenericConcreteRowChangeEventHandlerType), "An open generic type definition is required.");

		var closedGenericConcreteRowChangeEventType = openGenericConcreteRowChangeEventType.MakeGenericType(map.SourceEventType);
		var closedGenericInterfaceMessageHandlerType = typeof(IMessageHandler<>).MakeGenericType(closedGenericConcreteRowChangeEventType);
		var closedGenericConcreteRowChangeEventHandlerType = openGenericConcreteRowChangeEventHandlerType.MakeGenericType(map.SourceEventType, map.IntegrationEventType);

		builder.Services.AddTransient(closedGenericInterfaceMessageHandlerType, closedGenericConcreteRowChangeEventHandlerType);
		return builder;
	}

	public static IChatterBuilder WireHandlerExecutor(this IChatterBuilder builder, EventMappingConfigurationItem map)
	{
		_ = map.SourceEventType ?? throw new ArgumentNullException(nameof(map.SourceEventType), "A source event type is required.");
		_ = map.IntegrationEventType ?? throw new ArgumentNullException(nameof(map.IntegrationEventType), "An integration event type is required.");

		var closedIRowChangeHandlerExecutor = typeof(IRowChangeHandlerExecutor<,>).MakeGenericType(map.SourceEventType, map.IntegrationEventType);
		var closedRowChangeExecutor = typeof(RowChangeExecutor<,>).MakeGenericType(map.SourceEventType, map.IntegrationEventType);

		builder.Services.AddTransient(closedIRowChangeHandlerExecutor, closedRowChangeExecutor);

		return builder;
	}

	public static IServiceProvider UseChangeFeedSqlMigrations(this IServiceProvider provider)
	{
		var relayConfiguration = provider.GetRequiredService<EventMappingConfiguration>();

		if (relayConfiguration.Mappings is null)
			return provider;

		var mappingGroups = relayConfiguration.Mappings.GroupBy(d => (d.ConnectionString, d.DatabaseName, d.TableName, d.SourceEventType));
		foreach (var mappingGroup in mappingGroups)
		{
			provider.UseChangeFeedSqlMigrations(mappingGroup.Key.SourceEventType);
		}

		return provider;
	}
}
