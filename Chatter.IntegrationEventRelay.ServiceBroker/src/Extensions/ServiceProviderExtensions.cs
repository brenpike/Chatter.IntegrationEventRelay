using Chatter.SqlChangeFeed.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Extensions;

public static class ServiceProviderExtensions
{
	[Obsolete("Blocks on the asynchronous installation. Use UseChangeFeedSqlMigrationsAsync instead.")]
	public static IServiceProvider UseChangeFeedSqlMigrations(this IServiceProvider provider)
		=> provider.UseChangeFeedSqlMigrationsAsync().GetAwaiter().GetResult();

	/// <summary>
	/// Installs the trigger, queues and procedures of every SQL Service Broker change feed. Intended for development.
	/// </summary>
	public static async Task<IServiceProvider> UseChangeFeedSqlMigrationsAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
	{
		foreach (var feed in provider.GetServices<ServiceBrokerFeed>())
		{
			// No context capture, so the obsolete sync wrapper can't deadlock a single-threaded SynchronizationContext
			await provider.UseChangeFeedSqlMigrationsAsync(feed.SourceEventType, cancellationToken).ConfigureAwait(false);
		}

		return provider;
	}
}
