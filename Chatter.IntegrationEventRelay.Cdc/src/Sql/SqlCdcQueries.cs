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

	public async Task<IReadOnlyList<CdcChangeRow>> GetChangesAsync(string captureInstance, CdcLsn from, CdcLsn to, CancellationToken cancellationToken)
	{
		var function = $"cdc.{SqlIdentifier.Quote("fn_cdc_get_all_changes_" + captureInstance)}";
		var sql = $"""
			SELECT m.tran_end_time AS [{CommitTimeColumn}], c.*
			FROM {function}(@from, @to, N'all update old') AS c
			LEFT JOIN cdc.lsn_time_mapping AS m ON m.start_lsn = c.[__$start_lsn]
			ORDER BY c.[__$start_lsn], c.[__$seqval], c.[__$operation];
			""";

		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using var command = CreateCommand(connection, sql, Parameter("@from", from), Parameter("@to", to));
		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

		var startLsn = reader.GetOrdinal("__$start_lsn");
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
