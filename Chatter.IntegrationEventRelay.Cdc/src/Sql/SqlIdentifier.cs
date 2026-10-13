namespace Chatter.IntegrationEventRelay.Cdc.Sql;

internal static class SqlIdentifier
{
	/// <summary>
	/// Delimits <paramref name="name"/> as a SQL Server identifier, like <c>QUOTENAME</c>.
	/// </summary>
	public static string Quote(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("A SQL identifier can't be empty.", nameof(name));
		if (name.Length > 128)
			throw new ArgumentException($"SQL identifier '{name}' is longer than 128 characters.", nameof(name));

		return $"[{name.Replace("]", "]]")}]";
	}
}
