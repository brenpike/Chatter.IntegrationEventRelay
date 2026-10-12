using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.Core.Validation;

/// <summary>
/// Checks that every mapping's source exists and provides the mapping's required capabilities, and that every mapping
/// has exactly one mapper.
/// </summary>
public class MappingStartupCheck : IRelayStartupCheck
{
	public IEnumerable<string> Validate(IServiceProvider services)
	{
		var configuration = services.GetRequiredService<EventMappingConfiguration>();
		var sources = services.GetServices<IChangeSource>().ToList();

		foreach (var mapping in configuration.Mappings ?? Enumerable.Empty<EventMappingConfigurationItem>())
		{
			var description = Describe(mapping);
			var sourceName = EventMappingConfiguration.GetSourceName(mapping);
			var sourceType = configuration.GetSourceType(sourceName);

			if (sourceType is null)
			{
				yield return $"{description} reads from source '{sourceName}', which isn't configured under 'Sources'.";
				continue;
			}

			var source = sources.FirstOrDefault(s => string.Equals(s.Name, sourceName, StringComparison.OrdinalIgnoreCase));
			if (source is null)
			{
				yield return $"{description} reads from source '{sourceName}' of type '{sourceType}', but no change source adapter for that type is registered (for example UseServiceBrokerChangeSource()).";
				continue;
			}

			var missing = mapping.RequiredCapabilities & ~source.Capabilities;
			if (missing != ChangeSourceCapabilities.None)
				yield return $"{description} requires capabilities '{missing}' that source '{sourceName}' of type '{source.Type}' doesn't provide.";

			if (mapping.SourceEventType is null || mapping.IntegrationEventType is null)
				continue;

			var mapperType = GetMapperInterface(mapping.SourceChangeType).MakeGenericType(mapping.SourceEventType, mapping.IntegrationEventType);
			var mapperCount = services.GetServices(mapperType).Count();
			if (mapperCount != 1)
				yield return $"{description} needs exactly one implementation of '{Format(mapperType)}', found {mapperCount}.";
		}
	}

	internal static Type GetMapperInterface(RowOperation operation) => operation switch
	{
		RowOperation.Insert => typeof(IMapSourceInsertToIntegrationEvent<,>),
		RowOperation.Update => typeof(IMapSourceUpdateToIntegrationEvent<,>),
		RowOperation.Delete => typeof(IMapSourceDeleteToIntegrationEvent<,>),
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown row operation")
	};

	private static string Describe(EventMappingConfigurationItem mapping)
		=> $"Mapping of {mapping.SourceChangeType} on table '{mapping.TableName}' from '{mapping.SourceEventTypeName}' to '{mapping.IntegrationEventTypeName}'";

	private static string Format(Type type)
		=> type.IsGenericType
			? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Format))}>"
			: type.Name;
}
