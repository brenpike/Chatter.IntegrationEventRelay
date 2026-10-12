using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Builder;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.Core.Publishing;
using Chatter.IntegrationEventRelay.Core.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chatter.IntegrationEventRelay.Core.Extensions;

public static class ChatterBuilderExtensions
{
	/// <summary>
	/// Adds the integration event relay: mapping configuration, mappers, the row change processor and the publisher.
	/// Add a change source next, for example <c>.UseServiceBrokerChangeSource()</c>.
	/// </summary>
	public static IIntegrationEventRelayBuilder AddIntegrationEventRelay(this IChatterBuilder builder, EventMappingConfiguration eventMappingConfig)
	{
		_ = eventMappingConfig ?? throw new ArgumentNullException(nameof(eventMappingConfig));

		builder.Services.AddSingleton(eventMappingConfig);
		builder.Services.TryAddScoped<IIntegrationEventPublisher, BrokeredIntegrationEventPublisher>();
		builder.Services.TryAdd(ServiceDescriptor.Scoped(typeof(IRowChangeProcessor<>), typeof(RowChangeProcessor<>)));
		builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<IRelayStartupCheck, MappingStartupCheck>());
		builder.Services.AddHostedService<StartupValidationService>();

		var assemblies = builder.AssemblySourceFilter.Apply();
		var wiredMapperInterfaces = new HashSet<Type>();

		foreach (var map in eventMappingConfig.Mappings ?? new List<EventMappingConfigurationItem>())
		{
			map.IntegrationEventType = map.IntegrationEventTypeName?.GetTypeFromString(assemblies);
			map.SourceEventType = map.SourceEventTypeName?.GetTypeFromString(assemblies);
			_ = map.SourceEventType ?? throw new ArgumentNullException(nameof(map.SourceEventType), $"No type found for {nameof(map.SourceEventTypeName)} '{map.SourceEventTypeName}' for mapping to table '{map.TableName}'");
			_ = map.IntegrationEventType ?? throw new ArgumentNullException(nameof(map.IntegrationEventType), $"No type found for {nameof(map.IntegrationEventTypeName)} '{map.IntegrationEventTypeName}' for mapping to table '{map.TableName}'");

			if (!map.SourceEventType.IsClass)
				throw new ArgumentException($"Source event type '{map.SourceEventType.FullName}' must be a class.", nameof(eventMappingConfig));
			if (!map.IntegrationEventType.IsClass || !typeof(IEvent).IsAssignableFrom(map.IntegrationEventType))
				throw new ArgumentException($"Integration event type '{map.IntegrationEventType.FullName}' must be a class that implements {nameof(IEvent)}.", nameof(eventMappingConfig));

			builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped(
				typeof(IMappingInvoker<>).MakeGenericType(map.SourceEventType),
				typeof(MappingInvoker<,>).MakeGenericType(map.SourceEventType, map.IntegrationEventType)));

			var mapperInterface = MappingStartupCheck.GetMapperInterface(map.SourceChangeType);
			if (wiredMapperInterfaces.Add(mapperInterface.MakeGenericType(map.SourceEventType, map.IntegrationEventType)))
				builder.WireSourceToIntegrationEventMappers(mapperInterface, map);
		}

		return new IntegrationEventRelayBuilder(builder, eventMappingConfig);
	}

	/// <summary>
	/// Adds the integration event relay, binding its configuration from <paramref name="sectionName"/>.
	/// </summary>
	public static IIntegrationEventRelayBuilder AddIntegrationEventRelay(this IChatterBuilder builder, string sectionName = "IntegrationEventRelay")
	{
		var section = builder.Configuration.GetSection(sectionName);
		var eventMappingConfig = new EventMappingConfiguration();
		section.Bind(eventMappingConfig);

		foreach (var (name, source) in eventMappingConfig.Sources)
			source.Section = section.GetSection(nameof(EventMappingConfiguration.Sources)).GetSection(name);

		return builder.AddIntegrationEventRelay(eventMappingConfig);
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
				// publicOnly: false keeps internal mappers discoverable (Scrutor 3.x ignored its public-only default; newer versions enforce it)
				.AddClasses(c => c.AssignableTo(closeSourceToIntegrationEventMapperType), publicOnly: false)
				.UsingRegistrationStrategy(Scrutor.RegistrationStrategy.Append)
				// Only the requested interface: a mapper implementing several operations is scanned once per operation
				.As(closeSourceToIntegrationEventMapperType)
				.WithTransientLifetime());

		return builder;
	}
}
