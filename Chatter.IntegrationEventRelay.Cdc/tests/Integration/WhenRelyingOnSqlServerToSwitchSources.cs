using Chatter.CQRS;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// The SQL Server behaviour the switch scripts (<c>.scripts/cdc/switch-to-cdc.sql</c> and <c>switch-to-service-broker.sql</c>)
    /// rely on to move a table between SQL Service Broker and CDC without losing or repeating a change.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", Category.Integration)]
    public class WhenRelyingOnSqlServerToSwitchSources
    {
        /// <summary>
        /// The number of the latest log scan of CDC's capture job in the current database. Each scan is numbered (its
        /// <c>session_id</c>), but consecutive scans that find no transaction share one row of <c>sys.dm_cdc_log_scan_sessions</c>,
        /// numbered after the first of them, whose <c>empty_scan_count</c> counts them.
        /// </summary>
        public const string LatestLogScan = """
            (SELECT ISNULL(MAX(session_id + CASE WHEN empty_scan_count > 0 THEN empty_scan_count - 1 ELSE 0 END), 0)
                FROM sys.dm_cdc_log_scan_sessions WHERE session_id > 0)
            """;

        /// <summary>
        /// CDC's capture job has captured a transaction that committed after <c>@t</c>. It captures transactions in commit order,
        /// so it has captured every transaction committed before <c>@t</c>. <c>tran_end_time</c> is a <c>datetime</c>, rounded to
        /// 3.33 ms, hence the margin.
        /// </summary>
        public const string CaptureMappedATransactionAfter = """
            EXISTS (SELECT 1 FROM cdc.lsn_time_mapping WHERE tran_end_time > DATEADD(ms, 5, @t))
            """;

        /// <summary>
        /// A log scan that started after <c>@t</c> found no transaction to capture, so every transaction committed before
        /// <c>@t</c> had been captured. <c>@scanAtT</c> is <see cref="LatestLogScan"/> read at <c>@t</c>: scan <c>@scanAtT + 1</c>
        /// may have started before <c>@t</c>, but scan <c>@scanAtT + 2</c> started after it ended. A scan that found transactions
        /// isn't proof: it may have stopped at the capture job's <c>maxtrans</c>.
        /// </summary>
        public const string CaptureScannedNothingAfter = """
            EXISTS (SELECT 1 FROM sys.dm_cdc_log_scan_sessions
                WHERE session_id > 0 AND empty_scan_count > 0 AND session_id + empty_scan_count - 1 >= @scanAtT + 2)
            """;

        /// <summary>
        /// CDC's capture job has read the log past <c>@t</c>: every transaction committed before <c>@t</c> is in the change tables.
        /// Needs <c>@t datetime2 = SYSDATETIME()</c> and <c>@scanAtT int = </c><see cref="LatestLogScan"/>, read together.
        /// </summary>
        public const string CaptureReadPast = $"""
            ({CaptureMappedATransactionAfter}
                OR {CaptureScannedNothingAfter})
            """;

        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Blocked = TimeSpan.FromSeconds(3);

        private readonly SqlServerFixture _sql;
        private readonly string _table = $"Probe_{Guid.NewGuid():N}"[..20];

        public WhenRelyingOnSqlServerToSwitchSources(SqlServerFixture sql) => _sql = sql;

        [Fact]
        public async Task MustFoldConsecutiveEmptyLogScansIntoOneNumberedSession()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            await db.CreateCapturedTableAsync(_table);
            await WaitUntilAsync(async () => (await LatestSessionAsync(db)).EmptyScans > 0);
            var first = await LatestSessionAsync(db);

            await WaitUntilAsync(async () => (await LatestSessionAsync(db)).EmptyScans >= first.EmptyScans + 2);
            var later = await LatestSessionAsync(db);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await WaitUntilAsync(async () => (await LatestSessionAsync(db)).Id > later.Id);
            var captured = await db.ScalarAsync<int>($"SELECT MIN(session_id) FROM sys.dm_cdc_log_scan_sessions WHERE session_id > {later.Id};");
            var emptyScans = await db.ScalarAsync<int>($"SELECT empty_scan_count FROM sys.dm_cdc_log_scan_sessions WHERE session_id = {later.Id};");

            Assert.Equal(first.Id, later.Id);
            Assert.Equal(first.StartTime, later.StartTime);
            Assert.True(later.EndTime > first.EndTime, "The session's end time is that of its latest scan");
            Assert.Equal(later.Id + emptyScans, captured);
            Assert.Equal(0, await db.ScalarAsync<int>($"SELECT empty_scan_count FROM sys.dm_cdc_log_scan_sessions WHERE session_id = {captured};"));
        }

        [Fact]
        public async Task MustTellThatCaptureReadPastAPauseOnAnIdleDatabase()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            await WaitUntilAsync(async () => await db.ScalarAsync<int>(LatestLogScan) > 0);
            await db.ExecuteAsync("EXEC sys.sp_cdc_stop_job @job_type = N'capture';");
            await Task.Delay(TimeSpan.FromSeconds(2));
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1), (2); INSERT INTO dbo.[{_table}] (Id) VALUES (3);");

            var stopped = await CaptureReadPastNowAsync(db, timeoutSeconds: 10);
            Assert.Equal(0, await CountChangesAsync(db, captureInstance));
            await db.ExecuteAsync("EXEC sys.sp_cdc_start_job @job_type = N'capture';");
            var started = await CaptureReadPastNowAsync(db, (int)CaptureTimeout.TotalSeconds);

            Assert.False(stopped.ReadPast, "Capture read past the pause while its job was stopped");
            Assert.True(started.ReadPast, "Capture didn't read past the pause");
            Assert.False(started.Mapped, "Nothing committed after the pause, so only an empty scan can tell");
            Assert.Equal(3, await CountChangesAsync(db, captureInstance));
        }

        [Fact]
        public async Task MustTellThatCaptureReadPastAPauseOnABusyDatabase()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            await db.CreateCapturedTableAsync($"{_table}_busy");
            await WaitUntilAsync(async () => await db.ScalarAsync<int>(LatestLogScan) > 0);
            await db.ExecuteAsync("EXEC sys.sp_cdc_stop_job @job_type = N'capture';");
            await Task.Delay(TimeSpan.FromSeconds(2));
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1), (2); INSERT INTO dbo.[{_table}] (Id) VALUES (3);");
            using var stop = new CancellationTokenSource();
            var writer = WriteContinuouslyAsync(db, $"{_table}_busy", stop.Token);

            try
            {
                var stopped = await CaptureReadPastNowAsync(db, timeoutSeconds: 10);
                Assert.Equal(0, await CountChangesAsync(db, captureInstance));
                await db.ExecuteAsync("EXEC sys.sp_cdc_start_job @job_type = N'capture';");
                var started = await CaptureReadPastNowAsync(db, (int)CaptureTimeout.TotalSeconds);

                Assert.False(stopped.ReadPast, "Capture read past the pause while its job was stopped");
                Assert.True(started.ReadPast, "Capture didn't read past the pause");
                Assert.True(started.Mapped, "Transactions committed after the pause, so their capture tells");
                Assert.Equal(3, await CountChangesAsync(db, captureInstance));
            }
            finally
            {
                stop.Cancel();
                await writer;
            }
        }

        [Fact]
        public async Task MustSetTheOldestLsnOfANewCaptureInstanceSoASeedAfterThePauseLeavesNoGap()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            var oldestOnEnable = await new SqlCdcQueries(db.ConnectionString).GetMinLsnAsync(captureInstance, CancellationToken.None);

            var paused = await CaptureReadPastNowAsync(db, (int)CaptureTimeout.TotalSeconds);
            var seed = CdcLsn.FromBytes(await db.ScalarAsync<byte[]>($"""
                SELECT MAX(lsn) FROM (VALUES (sys.fn_cdc_get_max_lsn()), (sys.fn_cdc_decrement_lsn(sys.fn_cdc_get_min_lsn(N'{captureInstance}')))) AS seed (lsn);
                """));
            var oldest = await new SqlCdcQueries(db.ConnectionString).GetMinLsnAsync(captureInstance, CancellationToken.None);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await WaitUntilAsync(async () => await CountChangesAsync(db, captureInstance) > 0);

            Assert.True(paused.ReadPast, "Capture didn't read past the pause");
            Assert.True(oldestOnEnable is { IsZero: false }, "Enabling CDC on the table set its oldest LSN");
            Assert.True(oldest is { IsZero: false }, "The capture instance has an oldest LSN once capture read past the pause");
            Assert.True(seed.Increment() >= oldest.Value, $"Seed {seed} leaves a gap before {oldest}");
            Assert.True(CdcLsn.FromBytes(await db.ScalarAsync<byte[]>($"SELECT MIN(__$start_lsn) FROM cdc.[{captureInstance}_CT];")) > seed, "The seed is after the first change");
        }

        [Fact]
        public async Task MustMakeACountOfTheQueueWaitForAnUncommittedReceive()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            await db.CreateTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<ProbeEvent>(db.ConnectionString, _table);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            Assert.Equal(1, await feed.CountQueueAsync());

            await using var relay = new SqlConnection(db.ConnectionString);
            await relay.OpenAsync();
            await using (var transaction = (SqlTransaction)await relay.BeginTransactionAsync())
            {
                await ExecuteAsync(relay, transaction, $"RECEIVE TOP(1) conversation_handle FROM {feed.QualifiedQueue};");
                var count = feed.CountQueueAsync();

                Assert.NotSame(count, await Task.WhenAny(count, Task.Delay(Blocked)));
                await transaction.RollbackAsync();
                Assert.Equal(1, await count);
            }

            await using (var transaction = (SqlTransaction)await relay.BeginTransactionAsync())
            {
                await ExecuteAsync(relay, transaction, $"RECEIVE TOP(1) conversation_handle FROM {feed.QualifiedQueue};");
                await transaction.CommitAsync();
            }
            Assert.Equal(0, await feed.CountQueueAsync());
        }

        [Fact]
        public async Task MustDisableAndEnableTheTriggerUnderTheTableLockWhileTheRelayWaitsToReceive()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            await db.CreateTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<ProbeEvent>(db.ConnectionString, _table);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await WaitUntilAsync(async () => await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND wait_type = N'BROKER_RECEIVE_WAITFOR';") > 0);

            await RunUnderTheTableLockAsync(db, feed, $"DISABLE TRIGGER {feed.QualifiedTrigger} ON {feed.QualifiedTable};", writtenId: 1);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.True(await feed.IsTriggerDisabledAsync());
            Assert.Empty(receiver.RowIds);
            Assert.Equal(0, await feed.CountQueueAsync());

            await RunUnderTheTableLockAsync(db, feed, $"ENABLE TRIGGER {feed.QualifiedTrigger} ON {feed.QualifiedTable};", writtenId: 2);
            await WaitUntilAsync(() => receiver.RowIds.Count > 0);

            Assert.Equal(new[] { 2 }, receiver.RowIds);
            Assert.Equal(2, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{_table}];"));
        }

        [Fact]
        public async Task MustRunTheInstallProcedureInsideATransaction()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            await db.CreateTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<ProbeEvent>(db.ConnectionString, _table);
            await db.ExecuteAsync($"DISABLE TRIGGER {feed.QualifiedTrigger} ON {feed.QualifiedTable};");
            var installed = await TriggerIdAsync(db, feed);
            var install = $"EXEC dbo.[{feed.InstallProcedure}];";

            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, null, "SET XACT_ABORT ON;");
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                await ExecuteAsync(connection, transaction, install);

                Assert.Equal(installed, await TriggerIdAsync(connection, transaction, feed));
                Assert.True(await ScalarAsync<bool>(connection, transaction, $"SELECT is_disabled FROM sys.triggers WHERE object_id = {installed};"), "An unchanged trigger is left as it was");
                await transaction.CommitAsync();
            }

            await db.ExecuteAsync($"ALTER TABLE dbo.[{_table}] ADD Extra nvarchar(10) NULL;");
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                await ExecuteAsync(connection, transaction, install);
                var refreshed = await TriggerIdAsync(connection, transaction, feed);

                Assert.NotEqual(installed, refreshed);
                Assert.False(await ScalarAsync<bool>(connection, transaction, $"SELECT is_disabled FROM sys.triggers WHERE object_id = {refreshed};"), "A refreshed trigger is created enabled");
                Assert.Contains("[Extra]", await ScalarAsync<string>(connection, transaction, $"SELECT definition FROM sys.sql_modules WHERE object_id = {refreshed};"));
                await transaction.RollbackAsync();
            }

            Assert.Equal(installed, await TriggerIdAsync(db, feed));
            Assert.True(await feed.IsTriggerDisabledAsync());
        }

        [Fact]
        public async Task MustFireTheTriggerOnAStatementThatChangesNoRowAndFailItOnceAColumnItReadsIsDropped()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<ProbeEvent>(db.ConnectionString, _table);

            await db.ExecuteAsync($"DELETE FROM dbo.[{_table}] WHERE 1 = 0;");
            var sentForNoRow = await feed.CountQueueAsync();
            var body = await db.ScalarAsync<string>($"SELECT CAST(CAST(DECOMPRESS(message_body) AS varbinary(max)) AS nvarchar(max)) FROM {feed.QualifiedQueue};");
            await db.ExecuteAsync($"ALTER TABLE dbo.[{_table}] DROP COLUMN Name;");

            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, null, "SET XACT_ABORT ON;");
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                var e = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, transaction, $"DELETE FROM dbo.[{_table}] WHERE 1 = 0;"));
                Assert.Equal(207, e.Number);
                Assert.Equal(0, await ScalarAsync<int>(connection, null, "SELECT @@TRANCOUNT;"));
            }
            var paused = await CaptureReadPastNowAsync(db, (int)CaptureTimeout.TotalSeconds);

            Assert.Equal(1, sentForNoRow);
            Assert.Equal("{}", body);
            Assert.True(paused.ReadPast, "Capture didn't read past the pause");
            Assert.Equal(0, await CountChangesAsync(db, captureInstance));
        }

        [Fact]
        public async Task MustRefuseAVariableLockTimeout()
        {
            var e = await Assert.ThrowsAsync<SqlException>(() => _sql.ExecuteAsync("DECLARE @ms int = 1000; SET LOCK_TIMEOUT @ms;"));

            Assert.Equal(102, e.Number);
        }

        [Fact]
        public async Task MustLockAPartitionedTableWholeWithTablockDespiteAutoLockEscalation()
        {
            await _sql.ExecuteAsync($"""
                CREATE PARTITION FUNCTION [pf_{_table}] (int) AS RANGE LEFT FOR VALUES (100);
                CREATE PARTITION SCHEME [ps_{_table}] AS PARTITION [pf_{_table}] ALL TO ([PRIMARY]);
                CREATE TABLE dbo.[{_table}] (Id int NOT NULL PRIMARY KEY, Name nvarchar(50) NULL) ON [ps_{_table}] (Id);
                ALTER TABLE dbo.[{_table}] SET (LOCK_ESCALATION = AUTO);
                INSERT INTO dbo.[{_table}] (Id) VALUES (1), (200);
                """);

            await using var connection = new SqlConnection(_sql.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction, $"SELECT TOP (1) 1 FROM dbo.[{_table}] WITH (TABLOCK, HOLDLOCK);");
            var mode = await ScalarAsync<string>(connection, transaction, $"""
                SELECT request_mode FROM sys.dm_tran_locks
                WHERE request_session_id = @@SPID AND resource_type = N'OBJECT' AND resource_associated_entity_id = OBJECT_ID(N'dbo.[{_table}]');
                """);
            var writer = _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (300);");

            Assert.Equal("S", mode);
            Assert.NotSame(writer, await Task.WhenAny(writer, Task.Delay(Blocked)));
            await transaction.CommitAsync();
            await writer;
        }

        /// <summary>
        /// Takes the switch scripts' shared table lock, waits for writer of row <paramref name="writtenId"/> to queue behind it,
        /// runs <paramref name="statement"/>, commits and waits for the writer.
        /// </summary>
        private static async Task RunUnderTheTableLockAsync(SqlServerFixture.TestDatabase db, ServiceBrokerChangeFeed feed, string statement, int writtenId)
        {
            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, null, "SET XACT_ABORT ON; SET DEADLOCK_PRIORITY LOW;");
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, transaction, $"SELECT TOP (1) 1 FROM {feed.QualifiedTable} WITH (TABLOCK, HOLDLOCK);");
            var writer = db.ExecuteAsync($"INSERT INTO {feed.QualifiedTable} (Id) VALUES ({writtenId});");
            await WaitUntilAsync(async () => await db.ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE resource_database_id = DB_ID() AND resource_type = N'OBJECT' AND resource_associated_entity_id = OBJECT_ID(N'{feed.QualifiedTable}') AND request_status = N'WAIT';
                """) > 0);

            var run = ExecuteAsync(connection, transaction, statement);
            Assert.Same(run, await Task.WhenAny(run, Task.Delay(Blocked)));
            await run;
            Assert.False(writer.IsCompleted, "The writer didn't wait for the table lock");
            await transaction.CommitAsync();
            await writer;
        }

        /// <summary>
        /// Waits, for at most <paramref name="timeoutSeconds"/>, until CDC's capture job has read the log past now, as the switch
        /// scripts do.
        /// </summary>
        private static async Task<(bool ReadPast, bool Mapped)> CaptureReadPastNowAsync(SqlServerFixture.TestDatabase db, int timeoutSeconds)
        {
            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"""
                DECLARE @t datetime2 = SYSDATETIME();
                DECLARE @scanAtT int = {LatestLogScan};
                WHILE NOT {CaptureReadPast} AND DATEDIFF(second, @t, SYSDATETIME()) < {timeoutSeconds}
                    WAITFOR DELAY '00:00:00.200';
                SELECT CAST(CASE WHEN {CaptureReadPast} THEN 1 ELSE 0 END AS bit), CAST(CASE WHEN {CaptureMappedATransactionAfter} THEN 1 ELSE 0 END AS bit);
                """, connection) { CommandTimeout = timeoutSeconds + 30 };
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            return (reader.GetBoolean(0), reader.GetBoolean(1));
        }

        private static async Task<(int Id, DateTime StartTime, DateTime EndTime, int EmptyScans)> LatestSessionAsync(SqlServerFixture.TestDatabase db)
        {
            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT TOP (1) session_id, start_time, end_time, empty_scan_count FROM sys.dm_cdc_log_scan_sessions WHERE session_id > 0 ORDER BY session_id DESC;", connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return default;
            return (reader.GetInt32(0), reader.GetDateTime(1), reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2), reader.GetInt32(3));
        }

        private static async Task WriteContinuouslyAsync(SqlServerFixture.TestDatabase db, string table, CancellationToken stop)
        {
            for (var id = 1; !stop.IsCancellationRequested; id++)
            {
                await db.ExecuteAsync($"INSERT INTO dbo.[{table}] (Id) VALUES ({id});");
                await Task.Delay(100, CancellationToken.None);
            }
        }

        private static Task<int> CountChangesAsync(SqlServerFixture.TestDatabase db, string captureInstance)
            => db.ScalarAsync<int>($"SELECT COUNT(*) FROM cdc.[{captureInstance}_CT];");

        private static Task<int> TriggerIdAsync(SqlServerFixture.TestDatabase db, ServiceBrokerChangeFeed feed)
            => db.ScalarAsync<int>($"SELECT OBJECT_ID(N'{feed.QualifiedTrigger}');");

        private static Task<int> TriggerIdAsync(SqlConnection connection, SqlTransaction transaction, ServiceBrokerChangeFeed feed)
            => ScalarAsync<int>(connection, transaction, $"SELECT OBJECT_ID(N'{feed.QualifiedTrigger}');");

        private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<T> ScalarAsync<T>(SqlConnection connection, SqlTransaction transaction, string sql)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            return (T)await command.ExecuteScalarAsync();
        }

        private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow + CaptureTimeout;
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("SQL Server didn't reach the expected state in time. Is SQL Server Agent running?");
                await Task.Delay(500);
            }
        }

        public class ProbeEvent : IMessage { }
    }
}
