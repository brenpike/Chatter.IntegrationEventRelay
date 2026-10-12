using Chatter.IntegrationEventRelay.Cdc.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.Cdc.Extensions;

public static class ServiceProviderExtensions
{
	/// <summary>
	/// Creates the relay's checkpoint table and enables CDC on the database and on every mapped table, where missing.
	/// Intended for development: enabling CDC needs <c>sysadmin</c> (database) and <c>db_owner</c> (tables), and CDC
	/// captures changes only while SQL Server Agent runs. In production, use the scripts in <c>.scripts/cdc</c>.
	/// </summary>
	public static async Task<IServiceProvider> UseCdcSqlMigrationsAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
	{
		foreach (var target in provider.GetServices<CdcMigrationTarget>().Distinct())
		{
			await using var connection = new SqlConnection(target.ConnectionString);
			await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

			await using (var command = SqlCdcQueries.CreateCommand(connection, CdcSqlScripts.EnableCdc,
				SqlCdcQueries.Parameter("@tableSchema", target.TableSchema),
				SqlCdcQueries.Parameter("@tableName", target.TableName),
				SqlCdcQueries.Parameter("@captureInstance", target.CaptureInstance)))
			{
				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}

			await using (var command = SqlCdcQueries.CreateCommand(connection, CdcSqlScripts.CreateRelayTables, SqlCdcQueries.Parameter("@schema", target.Schema)))
			{
				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		return provider;
	}
}

/// <summary>
/// A capture instance and the database objects <see cref="ServiceProviderExtensions.UseCdcSqlMigrationsAsync"/> creates for it.
/// </summary>
internal sealed record CdcMigrationTarget(string ConnectionString, string Schema, string TableSchema, string TableName, string CaptureInstance);
