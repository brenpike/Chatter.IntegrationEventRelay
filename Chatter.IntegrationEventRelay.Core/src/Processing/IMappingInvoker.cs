using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;

namespace Chatter.IntegrationEventRelay.Core.Processing;

/// <summary>
/// Maps a change with the mapper for one integration event type and publishes the result.
/// </summary>
public interface IMappingInvoker<TSource> where TSource : class
{
	Type IntegrationEventType { get; }

	Task InvokeAsync(RowChange<TSource> change, EventMappingConfigurationItem mapping, CancellationToken cancellationToken);
}
