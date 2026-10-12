namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// Describes the change being mapped to an integration event.
/// </summary>
public sealed record RelayContext(
	string SourceName,
	RowOperation Operation,
	SourceTable? Table,
	ChangePosition? Position,
	DateTimeOffset? CommittedAt,
	CancellationToken CancellationToken)
{
	public static RelayContext From<TSource>(RowChange<TSource> change, CancellationToken cancellationToken) where TSource : class
		=> new(change.SourceName, change.Operation, change.Table, change.Position, change.CommittedAt, cancellationToken);
}
