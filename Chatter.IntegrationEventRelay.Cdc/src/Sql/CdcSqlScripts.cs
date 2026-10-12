namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// SQL that creates the relay's CDC tables and enables CDC. Mirrors <c>.scripts/cdc</c>.
/// </summary>
internal static class CdcSqlScripts
{
	public const string CheckpointsTable = "Chatter_RelayCheckpoints";

	/// <summary>
	/// Creates schema <c>@schema</c> and the checkpoint table in it, if missing.
	/// </summary>
	public const string CreateRelayTables = """
		IF SCHEMA_ID(@schema) IS NULL
			EXEC (N'CREATE SCHEMA ' + QUOTENAME(@schema));

		IF OBJECT_ID(QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints]', N'U') IS NULL
			EXEC (N'CREATE TABLE ' + QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints] (
				SourceName nvarchar(128) NOT NULL,
				CaptureInstance sysname NOT NULL,
				LastLsn binary(10) NOT NULL,
				UpdatedAt datetime2 NOT NULL,
				CONSTRAINT [PK_Chatter_RelayCheckpoints] PRIMARY KEY (SourceName, CaptureInstance)
			);');
		""";

	/// <summary>
	/// Enables CDC on the database and on table <c>@tableSchema.@tableName</c> as capture instance <c>@captureInstance</c>, if not already.
	/// </summary>
	public const string EnableCdc = """
		IF (SELECT is_cdc_enabled FROM sys.databases WHERE database_id = DB_ID()) = 0
			EXEC sys.sp_cdc_enable_db;

		IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance = @captureInstance)
			EXEC sys.sp_cdc_enable_table
				@source_schema = @tableSchema,
				@source_name = @tableName,
				@role_name = NULL,
				@capture_instance = @captureInstance;
		""";
}
