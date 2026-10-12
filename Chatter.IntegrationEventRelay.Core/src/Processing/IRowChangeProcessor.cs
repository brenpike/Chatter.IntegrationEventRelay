using Chatter.IntegrationEventRelay.Core.Abstractions;

namespace Chatter.IntegrationEventRelay.Core.Processing;

/// <summary>
/// Maps row changes to integration events and publishes them. Change sources call it and acknowledge a change only
/// once it returns; it throws if any mapping or publish fails.
/// </summary>
public interface IRowChangeProcessor<TSource> where TSource : class
{
	Task ProcessAsync(RowChange<TSource> change, CancellationToken cancellationToken);

	/// <summary>
	/// Processes <paramref name="changes"/> one at a time, in order, stopping at the first failure.
	/// </summary>
	async Task ProcessBatchAsync(IReadOnlyList<RowChange<TSource>> changes, CancellationToken cancellationToken)
	{
		foreach (var change in changes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ProcessAsync(change, cancellationToken).ConfigureAwait(false);
		}
	}
}
