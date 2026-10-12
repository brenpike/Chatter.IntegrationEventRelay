-- Enables SQL Server Change Data Capture (CDC) on the database and on a table, for Chatter.IntegrationEventRelay.Cdc.
-- Run once per table, in the source database, as a member of sysadmin (database) and db_owner (table).
-- CDC captures changes only while SQL Server Agent is running.
--
-- The relay reads capture instance '<TableSchema>_<TableName>' unless the mapping sets 'CaptureInstance'.
-- Not run by the demo's SQL container: it runs only the scripts directly in .scripts.

DECLARE @tableSchema sysname = N'dbo';
DECLARE @tableName sysname = N'Event1';
DECLARE @captureInstance sysname = @tableSchema + N'_' + @tableName;

IF (SELECT is_cdc_enabled FROM sys.databases WHERE database_id = DB_ID()) = 0
	EXEC sys.sp_cdc_enable_db;

IF NOT EXISTS (SELECT 1 FROM cdc.change_tables WHERE capture_instance = @captureInstance)
	EXEC sys.sp_cdc_enable_table
		@source_schema = @tableSchema,
		@source_name = @tableName,
		@role_name = NULL,
		@capture_instance = @captureInstance;
GO
