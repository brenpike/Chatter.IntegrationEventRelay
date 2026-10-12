using Microsoft.Extensions.Configuration;

namespace Chatter.IntegrationEventRelay.Core.Configuration;

/// <summary>
/// A named change source, configured under <c>IntegrationEventRelay:Sources:&lt;name&gt;</c>.
/// </summary>
public class ChangeSourceConfiguration
{
	/// <summary>
	/// The source type, for example "ServiceBroker". The matching change source adapter must be registered.
	/// </summary>
	public string? Type { get; set; }

	/// <summary>
	/// Optional. The connection string of the database to capture changes from. If not set, <see cref="EventMappingConfiguration.ConnectionString"/> is used.
	/// </summary>
	public string? ConnectionString { get; set; }

	/// <summary>
	/// The configuration section this source was bound from, so adapters can read their own settings. Null when configured in code.
	/// </summary>
	public IConfigurationSection? Section { get; set; }
}
