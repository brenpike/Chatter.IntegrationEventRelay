namespace Chatter.IntegrationEventRelay.Core.Abstractions;

/// <summary>
/// The table a change was captured from.
/// </summary>
public sealed record SourceTable(string Name, string? Schema = null, string? Database = null)
{
	/// <summary>
	/// True if <paramref name="tableName"/> names this table and, when both are known, <paramref name="databaseName"/>
	/// names its database. Comparison is case-insensitive, like SQL Server identifiers by default.
	/// </summary>
	public bool Matches(string? tableName, string? databaseName = null)
		=> string.Equals(Name, tableName, StringComparison.OrdinalIgnoreCase)
			&& (string.IsNullOrWhiteSpace(Database) || string.IsNullOrWhiteSpace(databaseName) || string.Equals(Database, databaseName, StringComparison.OrdinalIgnoreCase));

	public override string ToString() => string.Join('.', new[] { Database, Schema, Name }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
