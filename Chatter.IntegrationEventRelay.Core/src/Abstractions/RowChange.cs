namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// A change to a row of a source table, as delivered by a change source.
/// </summary>
/// <typeparam name="TSource">The type the changed row is mapped to.</typeparam>
/// <param name="SourceName">The name of the change source that captured the change.</param>
/// <param name="Operation">The kind of change.</param>
/// <param name="OldValue">The row before the change. Set for updates only.</param>
/// <param name="NewValue">The row after the change. For deletes, the deleted row.</param>
/// <param name="Table">The table the change was captured from, or null when the source can't tell.</param>
/// <param name="Position">The position of the change in the source's stream, or null when the source has no positions.</param>
/// <param name="CommittedAt">When the change was committed, or null when the source doesn't know.</param>
public sealed record RowChange<TSource>(
	string SourceName,
	RowOperation Operation,
	TSource? OldValue,
	TSource NewValue,
	SourceTable? Table,
	ChangePosition? Position,
	DateTimeOffset? CommittedAt)
	where TSource : class;
