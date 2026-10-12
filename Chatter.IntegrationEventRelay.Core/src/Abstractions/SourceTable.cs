namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// The table a change was captured from.
/// </summary>
public sealed record SourceTable(string Name, string? Schema = null, string? Database = null)
{
	/// <summary>
	/// True if <paramref name="tableName"/> names this table. Comparison is case-insensitive, like SQL Server identifiers by default.
	/// </summary>
	public bool Matches(string? tableName) => string.Equals(Name, tableName, StringComparison.OrdinalIgnoreCase);

	public override string ToString() => string.Join('.', new[] { Database, Schema, Name }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
