-- Enables SQL Server Change Data Capture (CDC) on the database and on a table, for Chatter.IntegrationEventRelay.Cdc.
-- Run once per table, in the source database, as a member of sysadmin (database) and db_owner (table).
-- CDC captures changes only while SQL Server Agent is running.
--
-- The relay reads capture instance '<TableSchema>_<TableName>' unless the mapping sets 'CaptureInstance'.
-- The demo's SQL container runs it from .scripts/01-Enable-Cdc-Event1.sql.

DECLARE @tableSchema sysname = N'dbo';
DECLARE @tableName sysname = N'Event1';
DECLARE @captureInstance sysname = @tableSchema + N'_' + @tableName;

IF (SELECT is_cdc_enabled FROM sys.databases WHERE database_id = DB_ID()) = 0
	EXEC sys.sp_cdc_enable_db;

DECLARE @capturedObjectId int = (SELECT source_object_id FROM cdc.change_tables WHERE capture_instance = @captureInstance);
IF @capturedObjectId IS NOT NULL AND @capturedObjectId <> OBJECT_ID(QUOTENAME(@tableSchema) + N'.' + QUOTENAME(@tableName))
	THROW 50000, N'The capture instance already exists for a different table. Use another capture instance name, or disable the existing one.', 1;

-- A partition switch moves rows without CDC recording them, so a partitioned table is captured with switching
-- disallowed. For a table that isn't partitioned, SQL Server only accepts 1.
DECLARE @allowPartitionSwitch bit = CASE WHEN EXISTS (
	SELECT 1 FROM sys.indexes AS i JOIN sys.partition_schemes AS ps ON ps.data_space_id = i.data_space_id
	WHERE i.object_id = OBJECT_ID(QUOTENAME(@tableSchema) + N'.' + QUOTENAME(@tableName)) AND i.index_id IN (0, 1)) THEN 0 ELSE 1 END;

IF @capturedObjectId IS NULL
	EXEC sys.sp_cdc_enable_table
		@source_schema = @tableSchema,
		@source_name = @tableName,
		@role_name = NULL,
		@capture_instance = @captureInstance,
		@allow_partition_switch = @allowPartitionSwitch;
GO

-- The relay's login needs SELECT on every captured column of each captured table (without it SQL Server reports no
-- changes rather than an error; the relay detects this and stops with an error), each table's change table, CDC's
-- metadata tables, and the relay's own tables (created by create-relay-tables.sql, in schema 'Chatter' unless
-- IntegrationEventRelay:Schema says otherwise). If the capture instance has a gating role (@role_name), the login must
-- also be a member of it.
--   GRANT SELECT (Id, StringData, BoolData, IntData, DeletedBy, DeletedAt) ON dbo.Event1 TO [relay_user]; -- captured columns
--   GRANT SELECT ON cdc.dbo_Event1_CT TO [relay_user];      -- and its change table
--   GRANT SELECT ON cdc.lsn_time_mapping TO [relay_user];
--   GRANT SELECT ON cdc.change_tables TO [relay_user];
--   GRANT SELECT ON cdc.captured_columns TO [relay_user];
--   GRANT SELECT ON cdc.index_columns TO [relay_user];
--   GRANT SELECT, INSERT, UPDATE ON Chatter.Chatter_RelayCheckpoints TO [relay_user];
--   GRANT SELECT, INSERT, UPDATE, DELETE ON Chatter.Chatter_RelayParkedChanges TO [relay_user];
-- A table-level GRANT SELECT also exposes columns left out of capture (@captured_column_list), and GRANT SELECT ON
-- SCHEMA::cdc exposes the change data of every other captured table.
