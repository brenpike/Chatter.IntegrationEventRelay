using Microsoft.Data.SqlClient;

namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// Stores checkpoints in <c>&lt;Schema&gt;.Chatter_RelayCheckpoints</c> in the source database.
/// </summary>
internal sealed class SqlCheckpointStore : ICdcCheckpointStore
{
	private readonly string _connectionString;
	private readonly string _table;

	public SqlCheckpointStore(string connectionString, string schema)
	{
		_connectionString = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentNullException(nameof(connectionString)) : connectionString;
		_table = $"{SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(CdcSqlScripts.CheckpointsTable)}";
	}

	public async Task<CdcLsn?> GetAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
	{
		var sql = $"SELECT LastLsn FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance;";

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = SqlCdcQueries.CreateCommand(connection, sql,
			SqlCdcQueries.Parameter("@sourceName", sourceName),
			SqlCdcQueries.Parameter("@captureInstance", captureInstance));
		return SqlCdcQueries.ToLsn(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
	}

	// Only moves forward, so a poller that lost its lease mid-batch can't undo the progress of the instance that took
	// over. One active poller per capture instance holds the lease, so the update-else-insert doesn't race in practice.
	public async Task SaveAsync(string sourceName, string captureInstance, CdcLsn lastLsn, CancellationToken cancellationToken)
	{
		var sql = $"""
			UPDATE {_table} SET LastLsn = @lastLsn, UpdatedAt = SYSUTCDATETIME()
			WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance AND LastLsn < @lastLsn;

			IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance)
				INSERT INTO {_table} (SourceName, CaptureInstance, LastLsn, UpdatedAt)
				VALUES (@sourceName, @captureInstance, @lastLsn, SYSUTCDATETIME());
			""";

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = SqlCdcQueries.CreateCommand(connection, sql,
			SqlCdcQueries.Parameter("@sourceName", sourceName),
			SqlCdcQueries.Parameter("@captureInstance", captureInstance),
			SqlCdcQueries.Parameter("@lastLsn", lastLsn));
		await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}
}
