-- Grants the relay's login what Chatter.IntegrationEventRelay.Cdc needs to relay one capture instance, and nothing more.
-- Run it in the source database, once per capture instance the login reads, after enable-cdc.sql and
-- create-relay-tables.sql, as a member of db_owner. Safe to run again. Set @whatIf = 1 to list the statements without
-- running them.
--
-- It grants SELECT on each captured column of the captured table (without it SQL Server reports no changes rather than
-- an error; the relay detects this and stops with an error), on the capture instance's change table and on CDC's
-- metadata tables cdc.lsn_time_mapping, cdc.change_tables and cdc.captured_columns; SELECT, INSERT and UPDATE of the
-- columns the relay changes on the checkpoint table; with @errorPolicy 'ParkRow', SELECT on cdc.index_columns and
-- SELECT, INSERT, DELETE and UPDATE of the columns the relay changes on the parked-change table; and, if the capture
-- instance has a gating role (@role_name), membership in it.
-- Chatter.IntegrationEventRelay.Cdc's integration tests run the relay under exactly these grants, for each error policy,
-- and check that it fails without any one of them.
--
-- A table-level GRANT SELECT also exposes columns left out of capture (@captured_column_list), and GRANT SELECT ON
-- SCHEMA::cdc exposes the change data of every other captured table. CDC's metadata tables show the names of every
-- capture instance and captured column in the database, though not their data.
-- sp_cdc_enable_db grants SELECT on cdc.lsn_time_mapping to public; this grants it to the login too, for databases
-- that revoke it.
-- Switching a source from ParkRow to Halt doesn't revoke the parked-change grants; revoke them by hand.

DECLARE @user sysname = N'relay_user';              -- the relay login's user in this database
DECLARE @captureInstance sysname = N'dbo_Event1';   -- the mapping's CaptureInstance, default '<TableSchema>_<TableName>'
DECLARE @schema sysname = N'Chatter';               -- the relay's IntegrationEventRelay:Schema
DECLARE @errorPolicy nvarchar(16) = N'Halt';        -- the source's ErrorPolicy: 'Halt' or 'ParkRow'
DECLARE @whatIf bit = 0;

IF DATABASE_PRINCIPAL_ID(@user) IS NULL
	THROW 50000, N'@user isn''t a user in this database. Create it first: CREATE USER [relay_user] FOR LOGIN [relay_login];', 1;
IF @errorPolicy IS NULL OR @errorPolicy NOT IN (N'Halt', N'ParkRow')
	THROW 50000, N'@errorPolicy must be ''Halt'' or ''ParkRow''.', 1;

DECLARE @sourceObjectId int, @changeTableId int, @roleName sysname;
SELECT @sourceObjectId = source_object_id, @changeTableId = object_id, @roleName = role_name
FROM cdc.change_tables
WHERE capture_instance = @captureInstance;
IF @sourceObjectId IS NULL
	THROW 50000, N'The capture instance doesn''t exist. Run enable-cdc.sql first, or check @captureInstance.', 1;

DECLARE @checkpoints nvarchar(300) = QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints]';
DECLARE @parkedChanges nvarchar(300) = QUOTENAME(@schema) + N'.[Chatter_RelayParkedChanges]';
IF OBJECT_ID(@checkpoints, N'U') IS NULL OR (@errorPolicy = N'ParkRow' AND OBJECT_ID(@parkedChanges, N'U') IS NULL)
	THROW 50000, N'The relay''s tables don''t exist in @schema. Run create-relay-tables.sql first, or check @schema.', 1;

DECLARE @capturedTable nvarchar(300) = QUOTENAME(OBJECT_SCHEMA_NAME(@sourceObjectId)) + N'.' + QUOTENAME(OBJECT_NAME(@sourceObjectId));
DECLARE @changeTable nvarchar(300) = QUOTENAME(OBJECT_SCHEMA_NAME(@changeTableId)) + N'.' + QUOTENAME(OBJECT_NAME(@changeTableId));
DECLARE @to nvarchar(200) = N' TO ' + QUOTENAME(@user) + N';';
DECLARE @statements TABLE (Id int IDENTITY PRIMARY KEY, Statement nvarchar(max) NOT NULL);

-- Column-level grants are one statement per column, so the tests can show each one is needed
INSERT INTO @statements (Statement)
SELECT N'GRANT SELECT (' + QUOTENAME(column_name) + N') ON ' + @capturedTable + @to
FROM cdc.captured_columns
WHERE object_id = @changeTableId
ORDER BY column_ordinal;

INSERT INTO @statements (Statement)
VALUES
	(N'GRANT SELECT ON ' + @changeTable + @to),
	(N'GRANT SELECT ON cdc.lsn_time_mapping' + @to),
	(N'GRANT SELECT ON cdc.change_tables' + @to),
	(N'GRANT SELECT ON cdc.captured_columns' + @to),
	(N'GRANT SELECT ON ' + @checkpoints + @to),
	(N'GRANT INSERT ON ' + @checkpoints + @to),
	(N'GRANT UPDATE ([LastLsn]) ON ' + @checkpoints + @to),
	(N'GRANT UPDATE ([UpdatedAt]) ON ' + @checkpoints + @to);

IF @errorPolicy = N'ParkRow'
	INSERT INTO @statements (Statement)
	VALUES
		(N'GRANT SELECT ON cdc.index_columns' + @to),  -- the row key that parked changes are grouped by
		(N'GRANT SELECT ON ' + @parkedChanges + @to),
		(N'GRANT INSERT ON ' + @parkedChanges + @to),
		(N'GRANT DELETE ON ' + @parkedChanges + @to),
		(N'GRANT UPDATE ([Error]) ON ' + @parkedChanges + @to),
		(N'GRANT UPDATE ([Attempts]) ON ' + @parkedChanges + @to),
		(N'GRANT UPDATE ([ReplayRequestedAt]) ON ' + @parkedChanges + @to);

IF @roleName IS NOT NULL AND ISNULL(IS_ROLEMEMBER(@roleName, @user), 0) = 0
	INSERT INTO @statements (Statement)
	VALUES (N'ALTER ROLE ' + QUOTENAME(@roleName) + N' ADD MEMBER ' + QUOTENAME(@user) + N';');

IF @whatIf = 1
BEGIN
	SELECT Statement FROM @statements ORDER BY Id;
	RETURN;
END

DECLARE @id int = 0, @statement nvarchar(max);
WHILE 1 = 1
BEGIN
	SELECT TOP (1) @id = Id, @statement = Statement FROM @statements WHERE Id > @id ORDER BY Id;
	IF @@ROWCOUNT = 0
		BREAK;
	EXEC sys.sp_executesql @statement;
END
