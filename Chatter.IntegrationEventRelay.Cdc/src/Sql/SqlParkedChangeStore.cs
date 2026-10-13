using Microsoft.Data.SqlClient;
using System.Data;

namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// Stores parked changes in <c>&lt;Schema&gt;.Chatter_RelayParkedChanges</c> in the source database.
/// </summary>
internal sealed class SqlParkedChangeStore : ICdcParkedChangeStore
{
	private const string Columns = "Id, RowKey, ChangeRowKey, Lsn, CommandId, SeqVal, Operation, Position, CommittedAt, Payload, Error, Attempts";

	private readonly string _connectionString;
	private readonly string _table;

	public SqlParkedChangeStore(string connectionString, string schema)
	{
		_connectionString = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentNullException(nameof(connectionString)) : connectionString;
		_table = $"{SqlIdentifier.Quote(schema)}.{SqlIdentifier.Quote(CdcSqlScripts.ParkedChangesTable)}";
	}

	public async Task<IReadOnlyDictionary<string, string>> GetParkedRowKeysAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
	{
		var keys = new Dictionary<string, string>(StringComparer.Ordinal);
		await ReadAsync($"SELECT DISTINCT RowKey, ChangeRowKey FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance;",
			r =>
			{
				keys[r.GetString(0)] = r.GetString(0);
				if (!r.IsDBNull(1))
					keys[r.GetString(1)] = r.GetString(0);
			},
			cancellationToken, Source(sourceName, captureInstance)).ConfigureAwait(false);
		return keys;
	}

	public Task ParkAsync(string sourceName, string captureInstance, ParkedChange change, CancellationToken cancellationToken)
		=> ExecuteAsync($"""
			IF NOT EXISTS (SELECT 1 FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance AND Position = @position)
				INSERT INTO {_table} (SourceName, CaptureInstance, RowKey, ChangeRowKey, Lsn, CommandId, SeqVal, Operation, Position, CommittedAt, Payload, Error, Attempts, ParkedAt)
				VALUES (@sourceName, @captureInstance, @rowKey, @changeRowKey, @lsn, @commandId, @seqVal, @operation, @position, @committedAt, @payload, @error, @attempts, SYSUTCDATETIME());
			""",
			cancellationToken,
			[.. Source(sourceName, captureInstance),
				SqlCdcQueries.Parameter("@rowKey", change.RowKey),
				new SqlParameter("@changeRowKey", SqlDbType.NVarChar, 400) { Value = (object?)change.ChangeRowKey ?? DBNull.Value },
				SqlCdcQueries.Parameter("@lsn", change.Lsn),
				SqlCdcQueries.Parameter("@commandId", change.CommandId),
				SqlCdcQueries.Parameter("@seqVal", change.SeqVal),
				SqlCdcQueries.Parameter("@operation", change.Operation),
				new SqlParameter("@position", SqlDbType.NVarChar, 400) { Value = change.Position },
				new SqlParameter("@committedAt", SqlDbType.DateTimeOffset) { Value = (object?)change.CommittedAt ?? DBNull.Value },
				new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = change.Payload },
				new SqlParameter("@error", SqlDbType.NVarChar, -1) { Value = change.Error },
				SqlCdcQueries.Parameter("@attempts", change.Attempts)]);

	public async Task<IReadOnlyList<string>> GetRowKeysToReplayAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
	{
		var keys = new List<string>();
		await ReadAsync($"SELECT DISTINCT RowKey FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance AND ReplayRequestedAt IS NOT NULL;",
			r => keys.Add(r.GetString(0)), cancellationToken, Source(sourceName, captureInstance)).ConfigureAwait(false);
		return keys;
	}

	public async Task<IReadOnlyList<ParkedChange>> GetParkedChangesAsync(string sourceName, string captureInstance, string rowKey, CancellationToken cancellationToken)
	{
		var changes = new List<ParkedChange>();
		await ReadAsync($"SELECT {Columns} FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance AND RowKey = @rowKey ORDER BY Lsn, CommandId, SeqVal, Id;",
			r => changes.Add(new ParkedChange(
				r.GetInt64(0),
				r.GetString(1),
				r.IsDBNull(2) ? null : r.GetString(2),
				CdcLsn.FromBytes((byte[])r.GetValue(3)),
				r.GetInt32(4),
				CdcLsn.FromBytes((byte[])r.GetValue(5)),
				r.GetString(6),
				r.GetString(7),
				r.IsDBNull(8) ? null : r.GetFieldValue<DateTimeOffset>(8),
				r.GetString(9),
				r.GetString(10),
				r.GetInt32(11))),
			cancellationToken,
			[.. Source(sourceName, captureInstance), SqlCdcQueries.Parameter("@rowKey", rowKey)]).ConfigureAwait(false);
		return changes;
	}

	public Task RemoveAsync(long id, CancellationToken cancellationToken)
		=> ExecuteAsync($"DELETE FROM {_table} WHERE Id = @id;", cancellationToken, new SqlParameter("@id", SqlDbType.BigInt) { Value = id });

	public Task RecordReplayFailureAsync(string sourceName, string captureInstance, ParkedChange change, string error, CancellationToken cancellationToken)
		=> ExecuteAsync($"""
			UPDATE {_table} SET Error = @error, Attempts = Attempts + 1 WHERE Id = @id;
			UPDATE {_table} SET ReplayRequestedAt = NULL WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance AND RowKey = @rowKey;
			""",
			cancellationToken,
			[.. Source(sourceName, captureInstance),
				SqlCdcQueries.Parameter("@rowKey", change.RowKey),
				new SqlParameter("@id", SqlDbType.BigInt) { Value = change.Id },
				new SqlParameter("@error", SqlDbType.NVarChar, -1) { Value = error }]);

	public async Task<(int Count, DateTime? OldestParkedAt)> GetSummaryAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
	{
		(int, DateTime?) summary = default;
		await ReadAsync($"SELECT COUNT(*), MIN(ParkedAt) FROM {_table} WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance;",
			r => summary = (r.GetInt32(0), r.IsDBNull(1) ? null : DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)),
			cancellationToken, Source(sourceName, captureInstance)).ConfigureAwait(false);
		return summary;
	}

	private static SqlParameter[] Source(string sourceName, string captureInstance)
		=> [SqlCdcQueries.Parameter("@sourceName", sourceName), SqlCdcQueries.Parameter("@captureInstance", captureInstance)];

	private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = SqlCdcQueries.CreateCommand(connection, sql, parameters);
		await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task ReadAsync(string sql, Action<SqlDataReader> read, CancellationToken cancellationToken, params SqlParameter[] parameters)
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = SqlCdcQueries.CreateCommand(connection, sql, parameters);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			read(reader);
	}
}
