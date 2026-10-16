-- Switches a table from SQL Service Broker (Chatter.SqlChangeFeed's trigger and queues) to CDC, so that each change is
-- relayed by exactly one of the two: SQL Service Broker relays every change committed before the switch, and CDC every
-- change committed after it, as source @sourceName.
--
-- Run it in the source database, on its own (not inside a transaction), as a member of db_owner. It needs no server
-- permission. Before it, enable CDC on the table and create the relay's tables (enable-cdc.sql and
-- create-relay-tables.sql) and grant the relay's login (grant-relay-permissions.sql). Leave the SQL Service Broker relay
-- running: the script waits for it to drain the table's queues. Don't relay @captureInstance as @sourceName with CDC
-- before the script has run: with no checkpoint, the relay starts from the oldest change, which SQL Service Broker also
-- relays. A CDC relay whose checkpoint switch-to-service-broker.sql sealed relays nothing and can keep running; it
-- starts from the new checkpoint at its next poll.
--
-- While it runs, don't deploy the relay or SqlChangeFeed migrations for the table, and don't change its SqlChangeFeed
-- services, queues, dead-letter queues or procedures. The script reads the table's triggers before it locks the table,
-- so a trigger created in between isn't switched: it stays enabled, and both sources relay its event type's changes.
-- The lock doesn't hold off changes to the services, queues, dead-letter queues or procedures, which aren't the
-- table's.
--
-- In one transaction, it:
-- 1. Takes a shared lock on the table. Writes to the table wait until the script ends; reads don't, until the last
--    step. A transaction that wrote to the table and is still open when the script starts must finish first.
-- 2. Waits until the SQL Service Broker relay has drained every SqlChangeFeed queue of the table, and CDC's capture job
--    has read the transaction log past the moment the lock was granted (the job scans the log every 5 seconds by
--    default). A message the relay has received but not committed counts until it commits, or goes back to the queue
--    if it rolls back, so the script waits for the relay's receive transaction (normally the time to publish one
--    message).
-- 3. Refuses if a dead-letter queue of the table holds messages: those are changes the relay gave up on, and nothing
--    relays them after the switch. With @keepDeadLetters = 1 it goes on and leaves them where they are.
-- 4. Saves @sourceName's checkpoint of @captureInstance at the latest change CDC has captured, replacing an earlier one
--    (or one sealed by switch-to-service-broker.sql), so CDC relays only changes committed after the switch.
-- 5. Disables SqlChangeFeed's trigger on the table, which waits for and briefly blocks reads of the table, and commits.
--
-- Its waits for the relay to drain the queues and for the capture job end within about @timeoutSeconds, and so do the
-- lock waits of the statements it runs holding the lock, except its reads of CDC's metadata: of cdc.lsn_time_mapping
-- while it waits for the capture job, and of the oldest and latest LSN it picks the checkpoint from. Past
-- @timeoutSeconds the script rolls back, changing nothing, and SQL Service Broker goes on relaying the table.
--
-- Those reads, and its checks before it locks the table, aren't bounded: one blocked by another transaction's lock
-- waits until that transaction ends. Before the lock that holds up no writes; a check waits, for example, while CDC's
-- cleanup job is moving the capture instance's oldest LSN in cdc.change_tables. Holding the lock, a transaction of
-- CDC's capture or cleanup job writing cdc.lsn_time_mapping or cdc.change_tables holds up the read, and writes to the
-- table with it, until it ends. So writes to the table wait about @timeoutSeconds at most, plus any such wait. If the
-- script doesn't finish, cancel it: it sets XACT_ABORT ON, so SQL Server rolls it back and nothing changes.
--
-- It refuses, changing nothing: inside a transaction; if the capture instance doesn't exist or captures another table;
-- if the table has no SqlChangeFeed trigger (Chatter_ChangeFeedTrigger_<SourceEventType>), or a trigger's service or
-- queue is missing or disabled; and if a SqlChangeFeed trigger on the table is disabled, so running it again after it
-- succeeded is refused (to switch back, run switch-to-service-broker.sql). A table switched to CDC by dropping
-- SqlChangeFeed's objects, as an earlier README described, is already on CDC.
--
-- It leaves SqlChangeFeed's queues, services, dead-letter queues and procedures, and the disabled trigger, in place, so
-- switch-to-service-broker.sql can switch the table back. The SQL Service Broker relay receives nothing more and can be
-- stopped, or its mapping removed. To remove SqlChangeFeed's objects for good, see the README ("Switching a table from
-- SQL Service Broker to CDC").

DECLARE @tableSchema sysname = N'dbo';
DECLARE @tableName sysname = N'Event2';
DECLARE @captureInstance sysname = N'dbo_Event2';    -- the CDC mapping's CaptureInstance, default '<TableSchema>_<TableName>'
DECLARE @sourceName nvarchar(128) = N'fakedb-cdc';   -- the CDC mapping's SourceName
DECLARE @relaySchema sysname = N'Chatter';           -- the relay's IntegrationEventRelay:Schema
DECLARE @keepDeadLetters bit = 0;
DECLARE @timeoutSeconds int = 300;

IF @@TRANCOUNT > 0
	THROW 50000, N'Run the script on its own, not inside a transaction: it commits or rolls back a transaction of its own.', 1;
-- Whatever the session set, before the first read: with implicit transactions a read would open a transaction that the
-- script's COMMIT leaves open, holding the switch and the table's lock, and a row count would stop a statement short of
-- the table's other triggers
SET IMPLICIT_TRANSACTIONS OFF;
SET ROWCOUNT 0;
IF @sourceName IS NULL OR @sourceName = N'' OR @keepDeadLetters IS NULL
	THROW 50000, N'Set @sourceName to the CDC mapping''s SourceName, and @keepDeadLetters to 0 or 1.', 1;
-- SET LOCK_TIMEOUT counts milliseconds in an int
IF @timeoutSeconds IS NULL OR @timeoutSeconds NOT BETWEEN 1 AND 86400
	THROW 50000, N'@timeoutSeconds must be between 1 and 86400.', 1;

-- nvarchar(max) throughout: QUOTENAME doubles each ']', so a fixed-size buffer could cut a valid name short
DECLARE @table nvarchar(max) = QUOTENAME(@tableSchema) + N'.' + QUOTENAME(@tableName);
DECLARE @tableId int = OBJECT_ID(@table, N'U');
IF @tableId IS NULL
	THROW 50000, N'The table doesn''t exist. Check @tableSchema and @tableName.', 1;
IF (SELECT is_cdc_enabled FROM sys.databases WHERE database_id = DB_ID()) = 0
	THROW 50000, N'CDC isn''t enabled in this database. Run enable-cdc.sql first.', 1;

DECLARE @capturedObjectId int = (SELECT source_object_id FROM cdc.change_tables WHERE capture_instance = @captureInstance);
IF @capturedObjectId IS NULL
	THROW 50000, N'The capture instance doesn''t exist. Run enable-cdc.sql first, or check @captureInstance.', 1;
IF @capturedObjectId <> @tableId
	THROW 50000, N'The capture instance captures another table. Check @captureInstance, @tableSchema and @tableName.', 1;

DECLARE @checkpoints nvarchar(max) = QUOTENAME(@relaySchema) + N'.[Chatter_RelayCheckpoints]';
IF OBJECT_ID(@checkpoints, N'U') IS NULL
	THROW 50000, N'The relay''s checkpoint table doesn''t exist in @relaySchema. Run create-relay-tables.sql first, or check @relaySchema.', 1;

-- SqlChangeFeed names its objects after the source event type T: trigger Chatter_ChangeFeedTrigger_T sends each change
-- to service Chatter_Service_T, and the relay dead-letters a change it gave up on to queue Chatter_DeadLetterQueue_T,
-- in the table's schema
DECLARE @feeds TABLE (TriggerName nvarchar(max) NOT NULL, IsDisabled bit NOT NULL, Queue nvarchar(max) NULL, QueueEnabled bit NULL, DeadLetterQueue nvarchar(max) NULL);
INSERT INTO @feeds (TriggerName, IsDisabled, Queue, QueueEnabled, DeadLetterQueue)
SELECT
	QUOTENAME(@tableSchema) + N'.' + QUOTENAME(tr.name),
	tr.is_disabled,
	QUOTENAME(SCHEMA_NAME(q.schema_id)) + N'.' + QUOTENAME(q.name),
	CASE WHEN q.is_receive_enabled = 1 AND q.is_enqueue_enabled = 1 THEN 1 ELSE 0 END,
	QUOTENAME(SCHEMA_NAME(dl.schema_id)) + N'.' + QUOTENAME(dl.name)
FROM sys.triggers AS tr
CROSS APPLY (SELECT SUBSTRING(tr.name, LEN(N'Chatter_ChangeFeedTrigger_') + 1, 128) COLLATE Latin1_General_BIN2 AS SourceEventType) AS feed
LEFT JOIN sys.services AS s ON s.name COLLATE Latin1_General_BIN2 = N'Chatter_Service_' + feed.SourceEventType
LEFT JOIN sys.service_queues AS q ON q.object_id = s.service_queue_id
LEFT JOIN sys.service_queues AS dl ON dl.schema_id = SCHEMA_ID(@tableSchema) AND dl.name COLLATE Latin1_General_BIN2 = N'Chatter_DeadLetterQueue_' + feed.SourceEventType
WHERE tr.parent_id = @tableId AND tr.name LIKE N'Chatter[_]ChangeFeedTrigger[_]%';

IF NOT EXISTS (SELECT 1 FROM @feeds)
	THROW 50000, N'The table has no SqlChangeFeed trigger (Chatter_ChangeFeedTrigger_<SourceEventType>), so it isn''t relayed through SQL Service Broker. A table switched by dropping SqlChangeFeed''s objects is already on CDC.', 1;
IF EXISTS (SELECT 1 FROM @feeds WHERE IsDisabled = 1)
	THROW 50000, N'A SqlChangeFeed trigger on the table is disabled: the table was already switched to CDC. To switch it back, run switch-to-service-broker.sql.', 1;
IF EXISTS (SELECT 1 FROM @feeds WHERE Queue IS NULL)
	THROW 50000, N'A SqlChangeFeed trigger on the table has no service Chatter_Service_<SourceEventType> on a queue, so its changes aren''t relayed. Reinstall the change feed (start the SQL Service Broker relay), or remove the trigger.', 1;
IF EXISTS (SELECT 1 FROM @feeds WHERE QueueEnabled = 0)
	THROW 50000, N'A SqlChangeFeed queue of the table is disabled, so the relay can''t drain it. SQL Service Broker disables a queue after five receives of it in a row roll back. Find out why, then enable it (ALTER QUEUE ... WITH STATUS = ON).', 1;
IF (SELECT is_broker_enabled FROM sys.databases WHERE database_id = DB_ID()) = 0
	THROW 50000, N'SQL Service Broker isn''t enabled in this database, so SqlChangeFeed''s queues can''t be drained.', 1;

DECLARE @countQueues nvarchar(max) = N'SET @messages = 0'
	+ (SELECT STRING_AGG(CAST(N' + (SELECT COUNT(*) FROM ' + Queue + N')' AS nvarchar(max)), N'') FROM @feeds) + N';';
DECLARE @countDeadLetters nvarchar(max) = N'SET @deadLetters = 0'
	+ ISNULL((SELECT STRING_AGG(CAST(N' + (SELECT COUNT(*) FROM ' + DeadLetterQueue + N')' AS nvarchar(max)), N'') FROM @feeds WHERE DeadLetterQueue IS NOT NULL), N'') + N';';
DECLARE @deadLetterError nvarchar(2048) = N'A dead-letter queue of the table (Chatter_DeadLetterQueue_<SourceEventType>) holds messages: changes the relay gave up on, '
	+ N'which nothing relays after the switch. Relay them or copy them out and empty the queue, or set @keepDeadLetters = 1 to leave them there.';

DECLARE @deadLetters int, @messages int;
EXEC sys.sp_executesql @countDeadLetters, N'@deadLetters int OUTPUT', @deadLetters = @deadLetters OUTPUT;
IF @deadLetters > 0 AND @keepDeadLetters = 0
	THROW 50000, @deadLetterError, 1;

DECLARE @deadline datetime2 = DATEADD(second, @timeoutSeconds, SYSDATETIME());
DECLARE @timeout nvarchar(10) = CAST(@timeoutSeconds AS nvarchar(10));
-- What it means if the statement waiting for a lock now runs out of time
DECLARE @lockTimeoutError nvarchar(2048);
DECLARE @error nvarchar(2048), @sql nvarchar(max), @seed binary(10);

SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
-- Whatever the session set: a read that kept its locks until the end, or read a snapshot, would hold off or miss the
-- relay draining the queues and CDC's capture job, so the script would time out
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRY
	BEGIN TRAN;

	-- SET LOCK_TIMEOUT only takes a constant, and a SET inside sp_executesql ends with it, so each statement that waits
	-- for a lock on the table, the queues or the relay's tables runs in sp_executesql with the time left as its lock
	-- timeout. A lock it doesn't get in time (error 1222) rolls the transaction back. The reads of CDC's metadata and
	-- the catalog aren't wrapped; see the header. XACT_ABORT stays on in there, so cancelling the script rolls it back
	-- wherever it is. The time left is read once and is never below 0: past the deadline it's 0 (don't wait), never -1,
	-- which waits for ever.

	-- Shared, so it waits for the table's open writing transactions and holds off new ones, but lets readers in
	SET @lockTimeoutError = N'A transaction writing to the table didn''t finish within ' + @timeout + N' seconds, so the script couldn''t lock the table. Nothing was changed.';
	SET @sql = N'SET LOCK_TIMEOUT ' + CAST((SELECT MAX(ms) FROM (VALUES (0), (DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline))) AS timeLeft (ms)) AS nvarchar(20)) + N';
		DECLARE @locked int; SELECT TOP (1) @locked = 1 FROM ' + @table + N' WITH (TABLOCK, HOLDLOCK);';
	EXEC sys.sp_executesql @sql;

	DECLARE @t datetime2 = SYSDATETIME();
	DECLARE @scanAtT int = (SELECT ISNULL(MAX(session_id + CASE WHEN empty_scan_count > 0 THEN empty_scan_count - 1 ELSE 0 END), 0)
		FROM sys.dm_cdc_log_scan_sessions WHERE session_id > 0);

	-- The queues only empty: the table's writers wait for the lock, and a count waits for a receive that hasn't committed.
	-- The dead-letter queues are counted after the queues, so a change the relay dead-lettered while they drained is seen.
	SET @lockTimeoutError = N'The SQL Service Broker relay didn''t drain the table''s SqlChangeFeed queues within ' + @timeout
		+ N' seconds: it held a received message without committing or rolling it back. Nothing was changed.';
	DECLARE @captured bit = 0;
	WHILE 1 = 1
	BEGIN
		-- CDC's capture job has read the log past @t: every change committed before it is in the change tables
		IF @captured = 0 AND (EXISTS (SELECT 1 FROM cdc.lsn_time_mapping WHERE tran_end_time > DATEADD(ms, 5, @t))
			OR EXISTS (SELECT 1 FROM sys.dm_cdc_log_scan_sessions
				WHERE session_id > 0 AND empty_scan_count > 0 AND session_id + empty_scan_count - 1 >= @scanAtT + 2))
			SET @captured = 1;

		IF @messages IS NULL OR @messages > 0
		BEGIN
			SET @sql = N'SET LOCK_TIMEOUT ' + CAST((SELECT MAX(ms) FROM (VALUES (0), (DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline))) AS timeLeft (ms)) AS nvarchar(20)) + N';
				' + @countQueues + N'
				' + @countDeadLetters;
			EXEC sys.sp_executesql @sql, N'@messages int OUTPUT, @deadLetters int OUTPUT', @messages = @messages OUTPUT, @deadLetters = @deadLetters OUTPUT;
		END

		IF @captured = 1 AND @messages = 0
			BREAK;
		IF SYSDATETIME() >= @deadline AND @messages > 0
		BEGIN
			SET @error = N'The SQL Service Broker relay didn''t drain the table''s SqlChangeFeed queues within ' + @timeout + N' seconds: '
				+ CAST(@messages AS nvarchar(10)) + N' message(s) are left. Is the relay running? Nothing was changed.';
			THROW 50000, @error, 1;
		END
		IF SYSDATETIME() >= @deadline
		BEGIN
			SET @error = N'CDC''s capture job didn''t read the transaction log past the switch within ' + @timeout
				+ N' seconds. Is SQL Server Agent running, and the capture job (sys.sp_cdc_start_job)? Nothing was changed.';
			THROW 50000, @error, 1;
		END
		WAITFOR DELAY '00:00:00.200';
	END

	IF @deadLetters > 0 AND @keepDeadLetters = 0
		THROW 50000, @deadLetterError, 1;

	-- The latest change CDC has captured, which is after every change committed before the lock: CDC relays from the
	-- change after it. Never before the capture instance's oldest change, which the relay would take for changes lost to
	-- CDC cleanup.
	SET @seed = (SELECT MAX(lsn) FROM (VALUES (sys.fn_cdc_get_max_lsn()), (sys.fn_cdc_decrement_lsn(sys.fn_cdc_get_min_lsn(@captureInstance)))) AS seed (lsn));
	IF @seed IS NULL OR @seed = 0x00000000000000000000
		THROW 50000, N'CDC has no LSN for the capture instance yet. Nothing was changed.', 1;

	SET @lockTimeoutError = N'A query reading the table didn''t finish within ' + @timeout + N' seconds, so the script couldn''t disable SqlChangeFeed''s trigger. Nothing was changed.';
	SET @sql = N'SET LOCK_TIMEOUT ' + CAST((SELECT MAX(ms) FROM (VALUES (0), (DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline))) AS timeLeft (ms)) AS nvarchar(20)) + N';
		UPDATE ' + @checkpoints + N' SET LastLsn = @seed, UpdatedAt = SYSUTCDATETIME()
		WHERE SourceName = @sourceName AND CaptureInstance = @captureInstance;
		IF @@ROWCOUNT = 0
			INSERT INTO ' + @checkpoints + N' (SourceName, CaptureInstance, LastLsn, UpdatedAt)
			VALUES (@sourceName, @captureInstance, @seed, SYSUTCDATETIME());
		' + (SELECT STRING_AGG(CAST(N'DISABLE TRIGGER ' + TriggerName + N' ON ' + @table + N';' AS nvarchar(max)), N' ') FROM @feeds);
	EXEC sys.sp_executesql @sql, N'@sourceName nvarchar(128), @captureInstance sysname, @seed binary(10)', @sourceName = @sourceName, @captureInstance = @captureInstance, @seed = @seed;

	COMMIT;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0
		ROLLBACK;
	IF ERROR_NUMBER() = 1222
		THROW 50000, @lockTimeoutError, 1;
	THROW;
END CATCH

SET @sql = N'Switched ' + @table + N' to CDC: disabled ' + CAST((SELECT COUNT(*) FROM @feeds) AS nvarchar(10))
	+ N' SqlChangeFeed trigger(s). Source ' + QUOTENAME(@sourceName, N'''') + N' relays capture instance ' + QUOTENAME(@captureInstance, N'''')
	+ N' from after LSN ' + CONVERT(nvarchar(22), @seed, 1) + N'.';
PRINT @sql;
