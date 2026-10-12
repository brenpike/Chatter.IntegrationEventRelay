using Microsoft.Data.SqlClient;
using System.Data;

namespace Chatter.IntegrationEventRelay.Cdc.Sql;

internal sealed class SqlCdcQueries : ICdcQueries
{
	private const string CommitTimeColumn = "__$chatter_tran_end_time";
	private const string MetadataColumnPrefix = "__$";

	private readonly string _connectionString;

	public SqlCdcQueries(string connectionString)
		=> _connectionString = string.IsNullOrWhiteSpace(connectionString) ? throw new ArgumentNullException(nameof(connectionString)) : connectionString;

	public async Task<CdcLsn> GetMinLsnAsync(string captureInstance, CancellationToken cancellationToken)
		=> ToLsn(await ScalarAsync("SELECT sys.fn_cdc_get_min_lsn(@captureInstance);", cancellationToken, Parameter("@captureInstance", captureInstance)).ConfigureAwait(false)) ?? CdcLsn.Zero;

	public async Task<bool> CaptureInstanceExistsAsync(string captureInstance, CancellationToken cancellationToken)
		=> await ScalarAsync("SELECT CASE WHEN EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance = @captureInstance) THEN 1 ELSE 0 END;",
			cancellationToken, Parameter("@captureInstance", captureInstance)).ConfigureAwait(false) is 1;

	public async Task<CdcLsn> GetMaxLsnAsync(CancellationToken cancellationToken)
		=> ToLsn(await ScalarAsync("SELECT sys.fn_cdc_get_max_lsn();", cancellationToken).ConfigureAwait(false)) ?? CdcLsn.Zero;

	// tran_id 0x00 rows are written periodically by the capture job and belong to no transaction
	public async Task<CdcLsn?> GetBatchUpperBoundAsync(CdcLsn from, CdcLsn max, int maxTransactions, CancellationToken cancellationToken)
		=> ToLsn(await ScalarAsync(
			"""
			SELECT MAX(batch.start_lsn)
			FROM (
				SELECT TOP (@maxTransactions) start_lsn
				FROM cdc.lsn_time_mapping
				WHERE start_lsn >= @from AND start_lsn <= @max AND tran_id <> 0x00
				ORDER BY start_lsn
			) AS batch;
			""",
			cancellationToken,
			Parameter("@maxTransactions", maxTransactions),
			Parameter("@from", from),
			Parameter("@max", max)).ConfigureAwait(false));

	// Reads the change table rather than cdc.fn_cdc_get_all_changes_<ci>, which doesn't return __$command_id: SQL Server
	// orders changes within a transaction by command, then sequence value (the table's unique clustered index is
	// __$start_lsn, __$command_id, __$seqval, __$operation), and __$seqval alone doesn't keep commands in order.
	// The caller keeps the range within the capture instance's min and max LSN, as the function would require.
	public async Task<IReadOnlyList<CdcChangeRow>> GetChangesAsync(string captureInstance, CdcLsn from, CdcLsn to, CancellationToken cancellationToken)
	{
		var changeTable = $"cdc.{SqlIdentifier.Quote(captureInstance + "_CT")}";
		var sql = $"""
			SELECT m.tran_end_time AS [{CommitTimeColumn}], c.*
			FROM {changeTable} AS c
			LEFT JOIN cdc.lsn_time_mapping AS m ON m.start_lsn = c.[__$start_lsn]
			WHERE c.[__$start_lsn] >= @from AND c.[__$start_lsn] <= @to
			ORDER BY c.[__$start_lsn], c.[__$command_id], c.[__$seqval], c.[__$operation];
			""";

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = CreateCommand(connection, sql, Parameter("@from", from), Parameter("@to", to));
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

		var startLsn = reader.GetOrdinal("__$start_lsn");
		var commandId = reader.GetOrdinal("__$command_id");
		var seqVal = reader.GetOrdinal("__$seqval");
		var operation = reader.GetOrdinal("__$operation");
		var commitTime = reader.GetOrdinal(CommitTimeColumn);
		var columns = Enumerable.Range(0, reader.FieldCount)
			.Select(i => (Ordinal: i, Name: reader.GetName(i)))
			.Where(c => !c.Name.StartsWith(MetadataColumnPrefix, StringComparison.Ordinal))
			.ToList();

		var rows = new List<CdcChangeRow>();
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var values = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
			foreach (var (ordinal, name) in columns)
				values[name] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);

			rows.Add(new CdcChangeRow(
				CdcLsn.FromBytes((byte[])reader.GetValue(startLsn)),
				reader.GetInt32(commandId),
				CdcLsn.FromBytes((byte[])reader.GetValue(seqVal)),
				reader.GetInt32(operation),
				reader.IsDBNull(commitTime) ? null : reader.GetDateTime(commitTime),
				values));
		}

		return rows;
	}

	public async Task<TimeSpan> GetServerUtcOffsetAsync(CancellationToken cancellationToken)
		=> TimeSpan.FromMinutes(Convert.ToInt32(await ScalarAsync("SELECT DATEPART(TZOFFSET, SYSDATETIMEOFFSET());", cancellationToken).ConfigureAwait(false)));

	public Task<IReadOnlyList<string>> GetCapturedColumnsAsync(string captureInstance, CancellationToken cancellationToken)
		=> ColumnNamesAsync("""
			SELECT cc.column_name
			FROM cdc.captured_columns AS cc
			JOIN cdc.change_tables AS ct ON ct.object_id = cc.object_id
			WHERE ct.capture_instance = @captureInstance
			ORDER BY cc.column_ordinal;
			""", captureInstance, cancellationToken);

	public async Task<CdcCapturedTable?> GetCapturedTableAsync(string captureInstance, CancellationToken cancellationToken)
	{
		const string sql = """
			SELECT OBJECT_SCHEMA_NAME(ct.source_object_id), OBJECT_NAME(ct.source_object_id), ct.partition_switch,
				CASE WHEN EXISTS (
					SELECT 1 FROM sys.indexes AS i JOIN sys.partition_schemes AS ps ON ps.data_space_id = i.data_space_id
					WHERE i.object_id = ct.source_object_id AND i.index_id IN (0, 1)) THEN 1 ELSE 0 END
			FROM cdc.change_tables AS ct
			WHERE ct.capture_instance = @captureInstance;
			""";

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = CreateCommand(connection, sql, Parameter("@captureInstance", captureInstance));
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0) || reader.IsDBNull(1))
			return null;
		return new CdcCapturedTable(reader.GetString(0), reader.GetString(1), Partitioned: reader.GetInt32(3) == 1, PartitionSwitchAllowed: reader.GetBoolean(2));
	}

	public Task<IReadOnlyList<string>> GetKeyColumnsAsync(string captureInstance, CancellationToken cancellationToken)
		=> ColumnNamesAsync("""
			SELECT ic.column_name
			FROM cdc.index_columns AS ic
			JOIN cdc.change_tables AS ct ON ct.object_id = ic.object_id
			WHERE ct.capture_instance = @captureInstance
			ORDER BY ic.index_ordinal;
			""", captureInstance, cancellationToken);

	private async Task<IReadOnlyList<string>> ColumnNamesAsync(string sql, string captureInstance, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = CreateCommand(connection, sql, Parameter("@captureInstance", captureInstance));
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

		var columns = new List<string>();
		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			columns.Add(reader.GetString(0));
		return columns;
	}

	private async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = CreateCommand(connection, sql, parameters);
		return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
	}

	internal static SqlCommand CreateCommand(SqlConnection connection, string sql, params SqlParameter[] parameters)
	{
		var command = new SqlCommand(sql, connection);
		command.Parameters.AddRange(parameters);
		return command;
	}

	internal static SqlParameter Parameter(string name, CdcLsn value)
		=> new(name, SqlDbType.Binary, CdcLsn.Length) { Value = value.ToArray() };

	// Sized for the longest value the relay stores (row keys and positions), so nothing is truncated
	internal static SqlParameter Parameter(string name, string value)
		=> new(name, SqlDbType.NVarChar, 400) { Value = value };

	internal static SqlParameter Parameter(string name, int value)
		=> new(name, SqlDbType.Int) { Value = value };

	internal static CdcLsn? ToLsn(object? value) => value is byte[] bytes ? CdcLsn.FromBytes(bytes) : null;
}
