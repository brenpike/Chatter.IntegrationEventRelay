namespace Chatter.IntegrationEventRelay.Cdc.Sql;

/// <summary>
/// SQL that creates the relay's CDC tables and enables CDC. Mirrors <c>.scripts/cdc</c>.
/// </summary>
internal static class CdcSqlScripts
{
	public const string CheckpointsTable = "Chatter_RelayCheckpoints";
	public const string ParkedChangesTable = "Chatter_RelayParkedChanges";

	/// <summary>
	/// Creates schema <c>@schema</c> and the checkpoint and parked change tables in it, if missing.
	/// </summary>
	public const string CreateRelayTables = """
		DECLARE @sql nvarchar(max);

		IF SCHEMA_ID(@schema) IS NULL
		BEGIN
			SET @sql = N'CREATE SCHEMA ' + QUOTENAME(@schema);
			EXEC sys.sp_executesql @sql;
		END

		IF OBJECT_ID(QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints]', N'U') IS NULL
		BEGIN
			SET @sql = N'CREATE TABLE ' + QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints] (
				SourceName nvarchar(128) NOT NULL,
				CaptureInstance sysname NOT NULL,
				LastLsn binary(10) NOT NULL,
				UpdatedAt datetime2 NOT NULL,
				CONSTRAINT [PK_Chatter_RelayCheckpoints] PRIMARY KEY (SourceName, CaptureInstance)
			);';
			EXEC sys.sp_executesql @sql;
		END

		IF OBJECT_ID(QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges]', N'U') IS NULL
		BEGIN
			SET @sql = N'CREATE TABLE ' + QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges] (
				Id bigint IDENTITY NOT NULL,
				SourceName nvarchar(128) NOT NULL,
				CaptureInstance sysname NOT NULL,
				RowKey nvarchar(400) NOT NULL,
				ChangeRowKey nvarchar(400) NULL,
				Lsn binary(10) NOT NULL,
				SeqVal binary(10) NOT NULL,
				Operation nvarchar(16) NOT NULL,
				Position nvarchar(400) NOT NULL,
				CommittedAt datetimeoffset NULL,
				Payload nvarchar(max) NOT NULL,
				Error nvarchar(max) NOT NULL,
				Attempts int NOT NULL,
				ParkedAt datetime2 NOT NULL,
				ReplayRequestedAt datetime2 NULL,
				CONSTRAINT [PK_Chatter_RelayParkedChanges] PRIMARY KEY (Id),
				CONSTRAINT [UQ_Chatter_RelayParkedChanges_Position] UNIQUE (SourceName, CaptureInstance, Position)
			);
			CREATE INDEX [IX_Chatter_RelayParkedChanges_RowKey] ON ' + QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges] (SourceName, CaptureInstance, RowKey, Lsn, SeqVal);';
			EXEC sys.sp_executesql @sql;
		END
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
