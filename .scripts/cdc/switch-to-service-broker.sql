-- Switches a table that Chatter.IntegrationEventRelay relays with the CDC change source back to the SQL Service Broker
-- change source (SqlChangeFeed), so that each change is relayed by exactly one of them: CDC relays every change
-- committed before the switch, and SQL Service Broker every change committed after it.
--
-- Run it in the source database as a member of db_owner (it needs no server permission), on a table that
-- switch-to-cdc.sql switched to CDC, while the CDC relay is running. Every CDC source of @captureInstance must have
-- saved its checkpoint in @relaySchema before the script runs, and no other may start until the CDC source is removed
-- from the table's mappings: the script waits for and seals only the checkpoints that exist. A source with no
-- checkpoint saves one at its first poll with StartPosition Latest, but with Earliest only once it has relayed its
-- first batch; one that saves it while the script runs can be sealed before it has relayed every change committed
-- before the switch, and one that saves it after the seal relays changes committed after the switch too, which SQL
-- Service Broker relays. Run it before deploying the relay with the table's mappings back on SQL Service Broker: that
-- relay's SqlChangeFeed migrations re-create a trigger whose table's columns have changed, enabled, and CDC would relay
-- the table's changes too until the script ran. Changes committed after the switch wait in the table's queue until that
-- relay reads it. Run it outside a transaction: it commits the switch itself, or rolls all of it back and changes
-- nothing.
--
-- It takes a shared lock on the table, so writes to it wait until the script ends (it waits, too, for transactions
-- already writing to it to finish); reads don't, except for a moment at the end, while it enables the triggers. Holding
-- the lock, it waits until CDC's capture job has read the log past the moment it took it, and then until every CDC
-- source with a checkpoint for @captureInstance in @relaySchema has relayed the capture instance's last change, refusing
-- before it waits and each time it checks if a source is on a gap. It then seals those checkpoints at LSN
-- 0xFFFFFFFFFFFFFFFFFFFE, past any LSN SQL Server will write, and enables SqlChangeFeed's triggers on the table, after
-- running each one's install procedure, if it still exists, to re-create a trigger whose table's columns have changed.
-- Last, it fires the triggers with a statement that changes no row and rolls that back, so a trigger that no longer
-- compiles fails the switch rather than every write after it. For that statement it disables the table's other INSTEAD
-- OF triggers, which would stop the triggers firing, and rolling the statement back enables them again.
--
-- Every wait, for locks included, ends within @timeoutSeconds: past it the script rolls back, changing nothing, and CDC
-- goes on relaying the table. Writes to the table wait about that long at most, plus the time the install procedures
-- and the table's triggers take to run once each at the end, which @timeoutSeconds doesn't cut short. Cancelling the
-- script rolls it back too.
--
-- The CDC relay keeps polling the capture instance, stays healthy with a lag of 0, and relays none of its later changes.
-- Remove the CDC source from the table's mappings when you redeploy; switch-to-cdc.sql replaces the seal if you switch
-- the table to CDC again. CDC keeps capturing the table: disable it with sys.sp_cdc_disable_table once no relay reads
-- the capture instance.
--
-- It refuses, changing nothing, when the table has no SqlChangeFeed trigger or one that's enabled (it isn't on CDC, or
-- was switched to CDC by dropping its change feed, which this script can't undo), the capture instance doesn't capture
-- the table, SQL Service Broker is disabled in the database or a trigger's service or queue is missing or disabled, no
-- CDC source has a checkpoint for the capture instance in @relaySchema, any change of the capture instance is parked
-- (replay or delete them first, also when one is parked while it waits), or a CDC source is on a gap, before it waits or
-- while it does: CDC cleanup has removed changes past its checkpoint, which nothing would relay after the switch (delete
-- the checkpoints of sources that no longer run; for a running source see "Gaps" under "Limitations" in the README). It
-- rolls back when the capture job or a CDC source doesn't catch up within @timeoutSeconds: delete the checkpoints of
-- sources that no longer run.

DECLARE @tableSchema sysname = N'dbo';
DECLARE @tableName sysname = N'Event2';
DECLARE @captureInstance sysname = N'dbo_Event2';   -- the CDC source mapping's CaptureInstance
DECLARE @relaySchema sysname = N'Chatter';          -- the relay's IntegrationEventRelay:Schema
DECLARE @timeoutSeconds int = 300;                  -- how long the script may wait, for locks included, while it holds up writes to the table

IF @@TRANCOUNT > 0
	THROW 50000, N'Run the script outside a transaction: it commits the switch itself, or rolls all of it back.', 1;
-- SET LOCK_TIMEOUT counts milliseconds in an int
IF @timeoutSeconds IS NULL OR @timeoutSeconds NOT BETWEEN 1 AND 86400
	THROW 50000, N'@timeoutSeconds must be between 1 and 86400.', 1;

-- nvarchar(max) throughout: QUOTENAME doubles each ']', so a fixed-size buffer could cut a valid name short
DECLARE @table nvarchar(max) = QUOTENAME(@tableSchema) + N'.' + QUOTENAME(@tableName);
DECLARE @tableId int = (SELECT object_id FROM sys.tables WHERE name = @tableName AND schema_id = SCHEMA_ID(@tableSchema));
IF @tableId IS NULL
	THROW 50000, N'The table doesn''t exist. Check @tableSchema and @tableName.', 1;

IF NOT EXISTS (SELECT * FROM sys.databases WHERE database_id = DB_ID() AND is_cdc_enabled = 1)
	THROW 50000, N'CDC isn''t enabled in the database, so the table isn''t relayed by the CDC change source.', 1;
DECLARE @sourceObjectId int, @changeTableId int;
SELECT @sourceObjectId = source_object_id, @changeTableId = object_id
FROM cdc.change_tables
WHERE capture_instance = @captureInstance;
IF @sourceObjectId IS NULL
	THROW 50000, N'The capture instance doesn''t exist. Check @captureInstance.', 1;
IF @sourceObjectId <> @tableId
	THROW 50000, N'The capture instance captures another table. Check @captureInstance, @tableSchema and @tableName.', 1;
DECLARE @changeTable nvarchar(max) = QUOTENAME(OBJECT_SCHEMA_NAME(@changeTableId)) + N'.' + QUOTENAME(OBJECT_NAME(@changeTableId));

-- SqlChangeFeed's triggers on the table, one per source event type <type>: [Chatter_ChangeFeedTrigger_<type>], in the
-- table's schema with install procedure [Chatter_InstallChangeFeed_<type>], sending to service [Chatter_Service_<type>]
DECLARE @feeds TABLE (Id int IDENTITY PRIMARY KEY, Suffix sysname NOT NULL, IsDisabled bit NOT NULL);
INSERT INTO @feeds (Suffix, IsDisabled)
SELECT SUBSTRING(name, LEN(N'Chatter_ChangeFeedTrigger_') + 1, 128), is_disabled
FROM sys.triggers
WHERE parent_id = @tableId AND name LIKE N'Chatter[_]ChangeFeedTrigger[_]%'
ORDER BY name;
IF NOT EXISTS (SELECT * FROM @feeds)
	THROW 50000, N'The table has no SqlChangeFeed trigger, so it can''t be switched back by this script: switch-to-cdc.sql keeps the change feed and disables its trigger, and the table wasn''t switched with it.', 1;
IF EXISTS (SELECT * FROM @feeds WHERE IsDisabled = 0)
	THROW 50000, N'A SqlChangeFeed trigger on the table is enabled, so the table is relayed by SQL Service Broker already. Nothing to do.', 1;

IF NOT EXISTS (SELECT * FROM sys.databases WHERE database_id = DB_ID() AND is_broker_enabled = 1)
	THROW 50000, N'SQL Service Broker is disabled in the database, so the triggers couldn''t send changes. Enable it first (ALTER DATABASE ... SET ENABLE_BROKER).', 1;
DECLARE @unusable nvarchar(max) = (
	SELECT STRING_AGG(QUOTENAME(N'Chatter_Service_' + f.Suffix), N', ')
	FROM @feeds AS f
	LEFT JOIN sys.services AS s ON s.name = (N'Chatter_Service_' + f.Suffix) COLLATE Latin1_General_100_BIN2
	LEFT JOIN sys.service_queues AS q ON q.object_id = s.service_queue_id
	WHERE q.object_id IS NULL OR q.is_receive_enabled = 0 OR q.is_enqueue_enabled = 0);
DECLARE @error nvarchar(2048);
IF @unusable IS NOT NULL
BEGIN
	SET @error = N'Service ' + @unusable + N' or its queue is missing or disabled, so the triggers couldn''t send changes. Re-create or enable it first.';
	THROW 50000, @error, 1;
END

DECLARE @checkpoints nvarchar(max) = QUOTENAME(@relaySchema) + N'.[Chatter_RelayCheckpoints]';
DECLARE @parkedChanges nvarchar(max) = QUOTENAME(@relaySchema) + N'.[Chatter_RelayParkedChanges]';
IF OBJECT_ID(@checkpoints, N'U') IS NULL
	THROW 50000, N'The relay''s tables don''t exist in @relaySchema. Check @relaySchema.', 1;
DECLARE @sources int;
DECLARE @sql nvarchar(max) = N'SELECT @sources = COUNT(*) FROM ' + @checkpoints + N' WHERE CaptureInstance = @captureInstance;';
EXEC sys.sp_executesql @sql, N'@captureInstance sysname, @sources int OUTPUT', @captureInstance, @sources OUTPUT;
IF @sources = 0
	THROW 50000, N'No CDC source has a checkpoint for the capture instance in @relaySchema, so none relays it. Check @captureInstance and @relaySchema.', 1;

DECLARE @parked int = 0;
DECLARE @countParked nvarchar(max) = N'SELECT @parked = COUNT(*) FROM ' + @parkedChanges + N' WHERE CaptureInstance = @captureInstance;';
DECLARE @parkedError nvarchar(2048) = N'Changes of the capture instance are parked in ' + @parkedChanges
	+ N', and nothing would relay them after the switch. Replay or delete them, then run the script again.';
IF OBJECT_ID(@parkedChanges, N'U') IS NOT NULL
	EXEC sys.sp_executesql @countParked, N'@captureInstance sysname, @parked int OUTPUT', @captureInstance, @parked OUTPUT;
IF @parked > 0
	THROW 50000, @parkedError, 1;

-- A CDC source is on a gap when CDC cleanup has removed changes after its checkpoint: the LSN after it is below the
-- capture instance's oldest. The oldest LSN is read first: it only grows, so a checkpoint read after it that's behind it
-- is behind for good, while a checkpoint read before it could be one a running source has moved past by the time
-- cleanup moves the oldest LSN past it. A zero checkpoint (start from the oldest change) never counts, nor does a sealed
-- one, past any LSN. No oldest LSN yet (NULL, before the capture job's first scan) counts none, and nor does a zero one
-- (the capture instance is gone), made NULL.
DECLARE @oldest binary(10), @gapped int = 0, @gappedLsn binary(10);
DECLARE @countGapped nvarchar(max) = N'SET @oldest = sys.fn_cdc_get_min_lsn(@captureInstance);
	SELECT @gapped = COUNT(*), @gappedLsn = MIN(LastLsn) FROM ' + @checkpoints + N'
	WHERE CaptureInstance = @captureInstance AND LastLsn <> 0x00000000000000000000
		AND LastLsn < sys.fn_cdc_decrement_lsn(NULLIF(@oldest, 0x00000000000000000000));';
DECLARE @gapError nvarchar(2048) = N'%d CDC source(s) of the capture instance are on a gap: the lowest LastLsn in %s is %s, and the capture instance''s oldest LSN is %s. '
	+ N'CDC cleanup has removed changes after their checkpoint, and nothing would relay them after the switch. Delete the checkpoints of sources that no longer run; '
	+ N'for a running source, see "Gaps" under "Limitations" in the README. Nothing was changed.';
EXEC sys.sp_executesql @countGapped, N'@captureInstance sysname, @oldest binary(10) OUTPUT, @gapped int OUTPUT, @gappedLsn binary(10) OUTPUT',
	@captureInstance, @oldest OUTPUT, @gapped OUTPUT, @gappedLsn OUTPUT;
IF @gapped > 0
BEGIN
	SET @error = FORMATMESSAGE(@gapError, @gapped, @checkpoints, CONVERT(nvarchar(22), @gappedLsn, 1), CONVERT(nvarchar(22), @oldest, 1));
	THROW 50000, @error, 1;
END

DECLARE @deadline datetime2 = DATEADD(second, @timeoutSeconds, SYSDATETIME());
DECLARE @timeout nvarchar(10) = CAST(@timeoutSeconds AS nvarchar(10));
-- What it means if the statement waiting for a lock now runs out of time
DECLARE @lockTimeoutError nvarchar(2048);

SET XACT_ABORT ON;
SET DEADLOCK_PRIORITY LOW;
DECLARE @sealed binary(10) = 0xFFFFFFFFFFFFFFFFFFFE;  -- one below the largest LSN, which the relay can't step past
DECLARE @probing bit = 0;
BEGIN TRY
	BEGIN TRANSACTION;

	-- SET LOCK_TIMEOUT only takes a constant, and a SET inside sp_executesql ends with it, so each statement that waits
	-- for a lock runs in sp_executesql with the time left as its lock timeout. A lock it doesn't get in time (error 1222)
	-- rolls the transaction back, rather than holding up the table's writers past @timeoutSeconds. XACT_ABORT stays on in
	-- there, so cancelling the script rolls it back wherever it is.

	-- Writers wait from here; transactions already writing to the table finish first
	SET @lockTimeoutError = N'A transaction writing to the table didn''t finish within ' + @timeout + N' seconds, so the script couldn''t lock the table. Nothing was changed.';
	SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
		DECLARE @locked int; SELECT TOP (1) @locked = 1 FROM ' + @table + N' WITH (TABLOCK, HOLDLOCK);';
	EXEC sys.sp_executesql @sql;

	DECLARE @t datetime2 = SYSDATETIME();
	DECLARE @scanAtT int = (SELECT ISNULL(MAX(session_id + CASE WHEN empty_scan_count > 0 THEN empty_scan_count - 1 ELSE 0 END), 0)
		FROM sys.dm_cdc_log_scan_sessions WHERE session_id > 0);

	-- Until the capture job has read the log past @t: it has captured a transaction committed after @t (it captures in
	-- commit order), or a log scan that started after @t found nothing to capture
	WHILE NOT (EXISTS (SELECT 1 FROM cdc.lsn_time_mapping WHERE tran_end_time > DATEADD(ms, 5, @t))
		OR EXISTS (SELECT 1 FROM sys.dm_cdc_log_scan_sessions
			WHERE session_id > 0 AND empty_scan_count > 0 AND session_id + empty_scan_count - 1 >= @scanAtT + 2))
	BEGIN
		IF SYSDATETIME() >= @deadline
		BEGIN
			SET @error = N'CDC''s capture job didn''t read the transaction log past the switch within ' + @timeout
				+ N' seconds. Is SQL Server Agent running, and the capture job (sys.sp_cdc_start_job)? Nothing was changed.';
			THROW 50000, @error, 1;
		END
		WAITFOR DELAY '00:00:00.200';
	END

	-- Until every CDC source of the capture instance has relayed its last change, refusing a source on a gap, which can't
	-- catch up. Without a change left (NULL), none is behind, but cleanup may have removed changes a source hadn't relayed.
	DECLARE @last binary(10), @behind int;
	SET @lockTimeoutError = N'A transaction writing to the capture instance''s change table (CDC''s capture or cleanup job) didn''t finish within ' + @timeout
		+ N' seconds, so the script couldn''t read its last change. Nothing was changed.';
	SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
		SELECT @last = MAX(__$start_lsn) FROM ' + @changeTable + N';';
	EXEC sys.sp_executesql @sql, N'@last binary(10) OUTPUT', @last OUTPUT;
	WHILE 1 = 1
	BEGIN
		SET @lockTimeoutError = N'A transaction writing to the relay''s checkpoints, or CDC''s cleanup job moving the capture instance''s oldest LSN, didn''t finish within ' + @timeout
			+ N' seconds, so the script couldn''t tell whether a CDC source is on a gap. Nothing was changed.';
		SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
			' + @countGapped;
		EXEC sys.sp_executesql @sql, N'@captureInstance sysname, @oldest binary(10) OUTPUT, @gapped int OUTPUT, @gappedLsn binary(10) OUTPUT',
			@captureInstance, @oldest OUTPUT, @gapped OUTPUT, @gappedLsn OUTPUT;
		IF @gapped > 0
		BEGIN
			SET @error = FORMATMESSAGE(@gapError, @gapped, @checkpoints, CONVERT(nvarchar(22), @gappedLsn, 1), CONVERT(nvarchar(22), @oldest, 1));
			THROW 50000, @error, 1;
		END
		SET @lockTimeoutError = N'A transaction writing to the relay''s checkpoints didn''t finish within ' + @timeout
			+ N' seconds, so the script couldn''t tell whether the CDC sources had caught up. Nothing was changed.';
		SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
			SELECT @behind = COUNT(*) FROM ' + @checkpoints + N' WHERE CaptureInstance = @captureInstance AND LastLsn < @last;';
		EXEC sys.sp_executesql @sql, N'@captureInstance sysname, @last binary(10), @behind int OUTPUT', @captureInstance, @last, @behind OUTPUT;
		IF @behind = 0
			BREAK;
		IF SYSDATETIME() >= @deadline
		BEGIN
			SET @error = CONVERT(nvarchar(11), @behind) + N' CDC source(s) didn''t relay the capture instance up to LSN ' + CONVERT(nvarchar(22), @last, 1)
				+ N' within ' + @timeout + N' seconds (see LastLsn in ' + @checkpoints + N'). Are they running, or halted on a change they can''t publish? Nothing was changed.';
			THROW 50000, @error, 1;
		END
		WAITFOR DELAY '00:00:00.200';
	END

	-- A change parked while the script waited
	SET @lockTimeoutError = N'A transaction writing to the relay''s parked changes didn''t finish within ' + @timeout
		+ N' seconds, so the script couldn''t tell whether a change of the capture instance is parked. Nothing was changed.';
	IF OBJECT_ID(@parkedChanges, N'U') IS NOT NULL
	BEGIN
		SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
			' + @countParked;
		EXEC sys.sp_executesql @sql, N'@captureInstance sysname, @parked int OUTPUT', @captureInstance, @parked OUTPUT;
	END
	IF @parked > 0
		THROW 50000, @parkedError, 1;

	DECLARE @sealedCount int;
	SET @lockTimeoutError = N'A transaction using the relay''s checkpoints didn''t finish within ' + @timeout
		+ N' seconds, so the script couldn''t seal them. Nothing was changed.';
	SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
		UPDATE ' + @checkpoints + N' SET LastLsn = @sealed, UpdatedAt = SYSUTCDATETIME() WHERE CaptureInstance = @captureInstance; SET @sealedCount = @@ROWCOUNT;';
	EXEC sys.sp_executesql @sql, N'@captureInstance sysname, @sealed binary(10), @sealedCount int OUTPUT', @captureInstance, @sealed, @sealedCount OUTPUT;

	-- The install procedure re-creates, enabled, a trigger whose table's columns have changed, and leaves any other as it is
	SET @lockTimeoutError = N'A query reading the table didn''t finish within ' + @timeout + N' seconds, so the script couldn''t enable SqlChangeFeed''s triggers. Nothing was changed.';
	DECLARE @id int = 0, @suffix sysname, @install nvarchar(max);
	WHILE 1 = 1
	BEGIN
		SELECT TOP (1) @id = Id, @suffix = Suffix FROM @feeds WHERE Id > @id ORDER BY Id;
		IF @@ROWCOUNT = 0
			BREAK;
		SET @install = QUOTENAME(@tableSchema) + N'.' + QUOTENAME(N'Chatter_InstallChangeFeed_' + @suffix);
		IF OBJECT_ID(@install, N'P') IS NOT NULL
		BEGIN
			SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
				EXEC ' + @install + N';';
			EXEC sys.sp_executesql @sql;
		END
		SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
			ENABLE TRIGGER ' + QUOTENAME(@tableSchema) + N'.' + QUOTENAME(N'Chatter_ChangeFeedTrigger_' + @suffix) + N' ON ' + @table + N';';
		EXEC sys.sp_executesql @sql;
	END

	-- Fires each trigger with a statement that changes no row, by the first of DELETE, UPDATE and INSERT it fires on. The
	-- triggers send a message even for no row, so this is rolled back to a savepoint. An enabled INSTEAD OF trigger on the
	-- table runs in place of the statement, and the triggers don't fire, so the table's others are disabled after the
	-- savepoint and rolling back to it enables them again. @probing is set after that, so failing to disable one isn't
	-- reported as a SqlChangeFeed trigger that fails.
	DECLARE @column nvarchar(max) = (
		SELECT TOP (1) QUOTENAME(name) FROM sys.columns
		WHERE object_id = @tableId AND is_computed = 0 AND is_identity = 0 AND generated_always_type = 0 AND system_type_id <> TYPE_ID(N'timestamp')
		ORDER BY column_id);
	DECLARE @probes TABLE (Id int IDENTITY PRIMARY KEY, Statement nvarchar(max) NULL);
	INSERT INTO @probes (Statement)
	SELECT CASE fires.FirstEvent
		WHEN 1 THEN N'DELETE FROM ' + @table + N' WHERE 1 = 0;'
		WHEN 2 THEN N'UPDATE ' + @table + N' SET ' + @column + N' = ' + @column + N' WHERE 1 = 0;'
		ELSE N'INSERT INTO ' + @table + N' (' + @column + N') SELECT ' + @column + N' FROM ' + @table + N' WHERE 1 = 0;'
		END
	FROM (
		SELECT DISTINCT MIN(CASE te.type_desc WHEN N'DELETE' THEN 1 WHEN N'UPDATE' THEN 2 ELSE 3 END) AS FirstEvent
		FROM sys.triggers AS tr
		JOIN sys.trigger_events AS te ON te.object_id = tr.object_id
		WHERE tr.parent_id = @tableId AND tr.name LIKE N'Chatter[_]ChangeFeedTrigger[_]%'
		GROUP BY tr.object_id) AS fires;
	IF EXISTS (SELECT * FROM @probes WHERE Statement IS NULL)
		THROW 50000, N'A SqlChangeFeed trigger fires only on INSERT or UPDATE, and the table has no column the script can update to fire it. Nothing was changed.', 1;

	SET @lockTimeoutError = N'A query reading the table didn''t finish within ' + @timeout + N' seconds, so the script couldn''t fire SqlChangeFeed''s triggers to check them. Nothing was changed.';
	DECLARE @disableInsteadOf nvarchar(max) = (
		SELECT STRING_AGG(CAST(N'DISABLE TRIGGER ' AS nvarchar(max)) + QUOTENAME(@tableSchema) + N'.' + QUOTENAME(name) + N' ON ' + @table + N';', N' ')
		FROM sys.triggers
		WHERE parent_id = @tableId AND is_instead_of_trigger = 1 AND is_disabled = 0 AND name NOT LIKE N'Chatter[_]ChangeFeedTrigger[_]%');
	SAVE TRANSACTION Probe;
	IF @disableInsteadOf IS NOT NULL
	BEGIN
		SET @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
			' + @disableInsteadOf;
		EXEC sys.sp_executesql @sql;
	END
	SET @probing = 1;
	SET @id = 0;
	WHILE 1 = 1
	BEGIN
		SELECT TOP (1) @id = Id, @sql = N'SET LOCK_TIMEOUT ' + CAST(CASE WHEN SYSDATETIME() < @deadline THEN DATEDIFF_BIG(millisecond, SYSDATETIME(), @deadline) ELSE 0 END AS nvarchar(20)) + N';
			' + Statement
		FROM @probes WHERE Id > @id ORDER BY Id;
		IF @@ROWCOUNT = 0
			BREAK;
		EXEC sys.sp_executesql @sql;
	END
	SET @probing = 0;
	ROLLBACK TRANSACTION Probe;

	IF EXISTS (SELECT * FROM @feeds AS f WHERE NOT EXISTS (
		SELECT * FROM sys.triggers WHERE parent_id = @tableId AND name = N'Chatter_ChangeFeedTrigger_' + f.Suffix AND is_disabled = 0))
		THROW 50000, N'A SqlChangeFeed trigger isn''t enabled after the switch. Nothing was changed.', 1;

	COMMIT TRANSACTION;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0
		ROLLBACK TRANSACTION;
	IF ERROR_NUMBER() = 1222
		THROW 50000, @lockTimeoutError, 1;
	IF @probing = 1
	BEGIN
		SET @error = N'A SqlChangeFeed trigger on the table fails (' + ERROR_MESSAGE() + N'), most likely because a column it reads changed while the table was on CDC, '
			+ N'and its install procedure, which would have re-created it, no longer exists. Re-create the trigger for the table''s current columns, disable it, '
			+ N'and run the script again. Nothing was changed.';
		THROW 50000, @error, 1;
	END;
	THROW;
END CATCH

DECLARE @enabledCount int = (SELECT COUNT(*) FROM @feeds);
PRINT N'Switched ' + @table + N' to SQL Service Broker: enabled ' + CONVERT(nvarchar(11), @enabledCount) + N' SqlChangeFeed trigger(s) and sealed '
	+ CONVERT(nvarchar(11), @sealedCount) + N' CDC checkpoint(s) of capture instance ' + QUOTENAME(@captureInstance) + N'.';
