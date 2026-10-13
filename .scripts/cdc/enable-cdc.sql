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

-- Then grant the relay's login what it needs with grant-relay-permissions.sql.
