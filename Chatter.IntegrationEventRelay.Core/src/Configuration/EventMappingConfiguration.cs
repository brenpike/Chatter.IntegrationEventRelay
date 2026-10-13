namespace Chatter.IntegrationEventRelay.Core.Configuration;

public class EventMappingConfiguration
{
	/// <summary>
	/// The source a mapping reads from when it doesn't name one: SQL Service Broker, using <see cref="ConnectionString"/>.
	/// </summary>
	public const string DefaultSourceName = "ServiceBroker";

	/// <summary>
	/// The type of the default source.
	/// </summary>
	public const string DefaultSourceType = "ServiceBroker";

	/// <summary>
	/// The default <see cref="Schema"/>.
	/// </summary>
	public const string DefaultSchema = "Chatter";

	/// <summary>
	/// The default connection string, used by sources and mappings that don't set their own.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// Optional. The schema of the database objects the relay owns, such as CDC checkpoints. Default "Chatter".
	/// </summary>
	/// <remarks>
	/// SQL Service Broker objects created by Chatter.SqlChangeFeed (trigger, queues, procedures) stay in the schema of
	/// the table they watch; SqlChangeFeed can't place them elsewhere.
	/// </remarks>
	public string Schema { get; set; } = DefaultSchema;

	/// <summary>
	/// Optional. Named change sources that mappings refer to by <see cref="EventMappingConfigurationItem.Source"/>.
	/// </summary>
	public Dictionary<string, ChangeSourceConfiguration> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	public IEnumerable<EventMappingConfigurationItem>? Mappings { get; set; }

	/// <summary>
	/// The name of the source <paramref name="mapping"/> reads from.
	/// </summary>
	public static string GetSourceName(EventMappingConfigurationItem mapping)
		=> string.IsNullOrWhiteSpace(mapping.Source) ? DefaultSourceName : mapping.Source;

	/// <summary>
	/// The type of the source named <paramref name="sourceName"/>, or null if no such source is configured.
	/// </summary>
	public string? GetSourceType(string sourceName)
	{
		if (Sources.TryGetValue(sourceName, out var source))
			return source.Type;

		return string.Equals(sourceName, DefaultSourceName, StringComparison.OrdinalIgnoreCase) ? DefaultSourceType : null;
	}

	/// <summary>
	/// The mappings that read from sources of type <paramref name="sourceType"/>.
	/// </summary>
	public IEnumerable<EventMappingConfigurationItem> GetMappingsForSourceType(string sourceType)
		=> (Mappings ?? Enumerable.Empty<EventMappingConfigurationItem>())
			.Where(m => string.Equals(GetSourceType(GetSourceName(m)), sourceType, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// The connection string for <paramref name="mapping"/>: its own, else its source's, else <see cref="ConnectionString"/>.
	/// </summary>
	public string? GetConnectionString(EventMappingConfigurationItem mapping)
	{
		if (!string.IsNullOrWhiteSpace(mapping.ConnectionString))
			return mapping.ConnectionString;

		if (Sources.TryGetValue(GetSourceName(mapping), out var source) && !string.IsNullOrWhiteSpace(source.ConnectionString))
			return source.ConnectionString;

		return ConnectionString;
	}
}
