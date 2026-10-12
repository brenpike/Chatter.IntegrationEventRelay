namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// The kind of change made to a row of a source table.
/// </summary>
public enum RowOperation
{
	Insert,
	Update,
	Delete
}
