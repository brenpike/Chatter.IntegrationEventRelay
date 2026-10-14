using Chatter.CQRS;
using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// <c>.scripts/cdc/switch-to-cdc.sql</c> moves a table from SQL Service Broker to CDC: SQL Service Broker relays each change
    /// committed before the switch, CDC each change committed after it, and no change is relayed by both or neither.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", Category.Integration)]
    public class WhenSwitchingToCdc
    {
        private const string SourceName = "switch-cdc";
        private const int ScriptTimeoutSeconds = 90;

        // CDC's capture job reads the log every few seconds
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Blocked = TimeSpan.FromSeconds(3);

        private readonly SqlServerFixture _sql;
        private readonly string _table = $"Switch_{Guid.NewGuid():N}"[..20];

        public WhenSwitchingToCdc(SqlServerFixture sql) => _sql = sql;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MustRelayEachChangeOnceWithSqlServiceBrokerBeforeTheSwitchAndWithCdcAfterIt(bool busy)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            // On a busy database CDC's capture job maps transactions committed after the switch; on an idle one only its
            // empty log scans tell that it read past the switch
            if (busy)
                await db.CreateCapturedTableAsync($"{_table}_busy");
            using var stop = new CancellationTokenSource();
            var writer = busy ? WriteContinuouslyAsync(db, $"{_table}_busy", stop.Token) : Task.CompletedTask;

            try
            {
                await InsertAsync(feed, 1, 2, 3);
                await SwitchAsync(db, captureInstance);
                var relayedBeforeTheSwitch = receiver.RowIds;
                await InsertAsync(feed, 4, 5, 6);

                Assert.Equal(new[] { 4, 5, 6 }, await RelayWithCdcAsync(db, captureInstance, inserted: 6));
                Assert.Equal(new[] { 1, 2, 3 }, relayedBeforeTheSwitch.OrderBy(id => id));
                Assert.Equal(relayedBeforeTheSwitch, receiver.RowIds);
                Assert.Equal(0, await feed.CountQueueAsync());
                Assert.True(await feed.IsTriggerDisabledAsync());
            }
            finally
            {
                stop.Cancel();
                await writer;
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MustWaitForATransactionOpenAtTheSwitchAndLeaveItsChangeToSqlServiceBroker(bool commit)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var writer = new SqlConnection(db.ConnectionString);
            await writer.OpenAsync();
            await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
            await ExecuteAsync(writer, transaction, $"INSERT INTO {feed.QualifiedTable} (Id) VALUES (1);");

            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(() => CountTableLocksAsync(db, feed, "WAIT"));
            await Task.Delay(Blocked);
            Assert.False(switching.IsCompleted, "The switch didn't wait for the open transaction");
            if (commit)
                await transaction.CommitAsync();
            else
                await transaction.RollbackAsync();
            await switching;
            var relayedBeforeTheSwitch = receiver.RowIds;
            await InsertAsync(feed, 2);

            Assert.Equal(new[] { 2 }, await RelayWithCdcAsync(db, captureInstance, inserted: commit ? 2 : 1));
            Assert.Equal(commit ? new[] { 1 } : Array.Empty<int>(), relayedBeforeTheSwitch);
            Assert.Equal(relayedBeforeTheSwitch, receiver.RowIds);
        }

        [Fact]
        public async Task MustHoldOffWritersUntilTheSwitchAndRelayTheirChangesWithCdc()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            var held = receiver.HoldNextAsync();
            await InsertAsync(feed, 1);
            await held;

            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(() => CountTableLocksAsync(db, feed, "GRANT"));
            var writing = InsertAsync(feed, 2);
            Assert.NotSame(writing, await Task.WhenAny(writing, Task.Delay(Blocked)));
            Assert.Equal(1, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {feed.QualifiedTable};"));
            Assert.False(switching.IsCompleted, "The switch didn't wait for the relay's receive");
            receiver.Release();
            await switching;
            await writing;

            Assert.Equal(new[] { 2 }, await RelayWithCdcAsync(db, captureInstance, inserted: 2));
            Assert.Equal(new[] { 1 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustWaitForAReceiveThatRollsBackUntilTheRelayReceivesItAgain()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            var held = receiver.HoldNextAsync();
            await InsertAsync(feed, 1);
            await held;

            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(() => CountTableLocksAsync(db, feed, "GRANT"));
            await Task.Delay(Blocked);
            Assert.False(switching.IsCompleted, "The switch didn't wait for the relay's receive");
            receiver.Release(commit: false);
            await switching;

            Assert.Equal(new[] { 1 }, receiver.RowIds);
            Assert.Equal(0, await feed.CountQueueAsync());
            Assert.True(await feed.IsTriggerDisabledAsync());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MustRollBackWhenTheRelayDoesNotDrainTheQueueInTime(bool relayHoldsAMessage)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            var receiver = relayHoldsAMessage ? ServiceBrokerReceiver.Start(feed) : null;
            var held = receiver?.HoldNextAsync();
            await InsertAsync(feed, 1);
            if (held is not null)
                await held;

            try
            {
                var e = await Assert.ThrowsAsync<SqlException>(() => SwitchAsync(db, captureInstance, timeoutSeconds: 5));

                Assert.Equal(50000, e.Number);
                Assert.Contains("didn't drain", e.Message);
                await AssertNothingChangedAsync(db, feed, captureInstance);
                var writing = InsertAsync(feed, 2);
                Assert.Same(writing, await Task.WhenAny(writing, Task.Delay(Blocked)));
                await writing;
                if (relayHoldsAMessage)
                {
                    receiver.Release();
                    await WaitUntilAsync(() => receiver.RowIds.Count == 2);
                    Assert.Equal(new[] { 1, 2 }, receiver.RowIds.OrderBy(id => id));
                }
                else
                {
                    Assert.Equal(2, await feed.CountQueueAsync());
                }
            }
            finally
            {
                if (receiver is not null)
                    await receiver.DisposeAsync();
            }
        }

        [Fact]
        public async Task MustRollBackWhenATransactionOpenAtTheSwitchDoesNotFinishInTime()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var writer = new SqlConnection(db.ConnectionString);
            await writer.OpenAsync();
            await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
            await ExecuteAsync(writer, transaction, $"INSERT INTO {feed.QualifiedTable} (Id) VALUES (1);");

            var e = await Assert.ThrowsAsync<SqlException>(() => SwitchAsync(db, captureInstance, timeoutSeconds: 5));
            await transaction.CommitAsync();

            Assert.Equal(50000, e.Number);
            Assert.Contains("couldn't lock the table", e.Message);
            await AssertNothingChangedAsync(db, feed, captureInstance);
            Assert.Equal(1, await feed.CountQueueAsync());
        }

        [Fact]
        public async Task MustRollBackWhenCancelled()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            var held = receiver.HoldNextAsync();
            await InsertAsync(feed, 1);
            await held;
            using var cancel = new CancellationTokenSource();
            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(SwitchScripts.RenderToCdc("dbo", _table, captureInstance, SourceName, timeoutSeconds: ScriptTimeoutSeconds), connection);

            var switching = command.ExecuteNonQueryAsync(cancel.Token);
            await WaitUntilAsync(() => CountTableLocksAsync(db, feed, "GRANT"));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<Exception>(() => switching);

            Assert.Equal(0, await ScalarAsync<int>(connection, "SELECT @@TRANCOUNT;"));
            await AssertNothingChangedAsync(db, feed, captureInstance);
            var writing = InsertAsync(feed, 2);
            Assert.Same(writing, await Task.WhenAny(writing, Task.Delay(Blocked)));
            await writing;
            receiver.Release();
            await WaitUntilAsync(() => receiver.RowIds.Count == 2);
        }

        [Fact]
        public async Task MustRollBackWhenCaptureDoesNotReadPastTheSwitchInTime()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await WaitUntilAsync(async () => await db.ScalarAsync<int>(WhenRelyingOnSqlServerToSwitchSources.LatestLogScan) > 0);
            await db.ExecuteAsync("EXEC sys.sp_cdc_stop_job @job_type = N'capture';");
            await Task.Delay(TimeSpan.FromSeconds(2));

            var e = await Assert.ThrowsAsync<SqlException>(() => SwitchAsync(db, captureInstance, timeoutSeconds: 5));

            Assert.Equal(50000, e.Number);
            Assert.Contains("capture job", e.Message);
            await AssertNothingChangedAsync(db, feed, captureInstance);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task MustRefuseToSwitchWithDeadLettersUnlessTheyAreKept(bool keepDeadLetters, bool deadLetteredDuringTheSwitch)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            Task<Exception> switching;
            if (deadLetteredDuringTheSwitch)
            {
                // The relay gives up on a change while the switch waits for the queue to drain
                var held = receiver.HoldNextAsync();
                await InsertAsync(feed, 1);
                await held;
                switching = Record.ExceptionAsync(() => SwitchAsync(db, captureInstance, keepDeadLetters));
                await WaitUntilAsync(() => CountTableLocksAsync(db, feed, "GRANT"));
                await feed.SendToDeadLetterServiceAsync("{}");
                receiver.Release();
            }
            else
            {
                await feed.SendToDeadLetterServiceAsync("{}");
                switching = Record.ExceptionAsync(() => SwitchAsync(db, captureInstance, keepDeadLetters));
            }

            var e = await switching;

            Assert.Equal(1, await feed.CountDeadLetterQueueAsync());
            if (keepDeadLetters)
            {
                Assert.Null(e);
                Assert.True(await feed.IsTriggerDisabledAsync());
                return;
            }
            Assert.Equal(50000, Assert.IsType<SqlException>(e).Number);
            Assert.Contains("dead-letter queue", e.Message);
            await AssertNothingChangedAsync(db, feed, captureInstance);
        }

        [Fact]
        public async Task MustSwitchEverySourceEventTypeOfTheTable()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            var otherFeed = await ServiceBrokerChangeFeed.InstallAsync<OtherSwitchedEvent>(db.ConnectionString, _table);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var otherReceiver = ServiceBrokerReceiver.Start(otherFeed);
            var held = otherReceiver.HoldNextAsync();
            await InsertAsync(feed, 1, 2);
            await held;

            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(() => CountTableLocksAsync(db, feed, "GRANT"));
            await Task.Delay(Blocked);
            Assert.False(switching.IsCompleted, "The switch didn't wait for the second queue to drain");
            otherReceiver.Release();
            await switching;
            var relayedBeforeTheSwitch = receiver.RowIds;
            var otherRelayedBeforeTheSwitch = otherReceiver.RowIds;
            await InsertAsync(feed, 3);

            Assert.Equal(new[] { 3 }, await RelayWithCdcAsync(db, captureInstance, inserted: 3));
            Assert.Equal(new[] { 1, 2 }, relayedBeforeTheSwitch.OrderBy(id => id));
            Assert.Equal(new[] { 1, 2 }, otherRelayedBeforeTheSwitch.OrderBy(id => id));
            Assert.Equal(relayedBeforeTheSwitch, receiver.RowIds);
            Assert.Equal(otherRelayedBeforeTheSwitch, otherReceiver.RowIds);
            Assert.True(await feed.IsTriggerDisabledAsync());
            Assert.True(await otherFeed.IsTriggerDisabledAsync());
        }

        [Theory]
        [InlineData("not on SQL Service Broker", "no SqlChangeFeed trigger")]
        [InlineData("already switched", "already switched")]
        [InlineData("missing capture instance", "capture instance doesn't exist")]
        [InlineData("capture instance of another table", "captures another table")]
        [InlineData("queue disabled", "queue of the table is disabled")]
        [InlineData("inside a transaction", "not inside a transaction")]
        public async Task MustRefuseWithoutChangingAnything(string situation, string reason)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            var feed = situation == "not on SQL Service Broker"
                ? new ServiceBrokerChangeFeed(db.ConnectionString, "dbo", _table, nameof(SwitchedEvent))
                : await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table);
            var script = SwitchScripts.RenderToCdc("dbo", _table, captureInstance, SourceName, timeoutSeconds: ScriptTimeoutSeconds);
            byte[] switchedAt = null;
            switch (situation)
            {
                case "already switched":
                    await SwitchAsync(db, captureInstance);
                    switchedAt = await CheckpointAsync(db, captureInstance);
                    break;
                case "missing capture instance":
                    script = SwitchScripts.RenderToCdc("dbo", _table, $"{captureInstance}_missing", SourceName);
                    break;
                case "capture instance of another table":
                    script = SwitchScripts.RenderToCdc("dbo", _table, await db.CreateCapturedTableAsync($"{_table}_other"), SourceName);
                    break;
                case "queue disabled":
                    await db.ExecuteAsync($"ALTER QUEUE {feed.QualifiedQueue} WITH STATUS = OFF;");
                    break;
                case "inside a transaction":
                    script = "BEGIN TRAN;\n" + script;
                    break;
            }

            var e = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(db.ConnectionString, script, ScriptTimeoutSeconds));

            Assert.Equal(50000, e.Number);
            Assert.Contains(reason, e.Message);
            Assert.Equal(switchedAt, await CheckpointAsync(db, captureInstance));
            if (situation != "not on SQL Service Broker")
                Assert.Equal(switchedAt is not null, await feed.IsTriggerDisabledAsync());
        }

        [Theory]
        [InlineData("0xFFFFFFFFFFFFFFFFFFFE")]
        [InlineData("0x00000000000000000001")]
        public async Task MustReplaceAnEarlierOrSealedCheckpoint(string earlier)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await db.ExecuteAsync($"""
                INSERT INTO Chatter.Chatter_RelayCheckpoints (SourceName, CaptureInstance, LastLsn, UpdatedAt)
                VALUES (N'{SourceName}', N'{captureInstance}', {earlier}, SYSUTCDATETIME());
                """);
            await InsertAsync(feed, 1);

            await SwitchAsync(db, captureInstance);
            await InsertAsync(feed, 2);

            Assert.NotEqual(Convert.FromHexString(earlier[2..]), await CheckpointAsync(db, captureInstance));
            Assert.Equal(new[] { 2 }, await RelayWithCdcAsync(db, captureInstance, inserted: 2));
            Assert.Equal(new[] { 1 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustSeedACaptureInstanceEnabledJustBeforeTheSwitchWithoutAGap()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            await db.CreateTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await InsertAsync(feed, 1);

            var captureInstance = await db.EnableCdcAsync(_table);
            await SwitchAsync(db, captureInstance);
            await InsertAsync(feed, 2);

            Assert.Equal(new[] { 2 }, await RelayWithCdcAsync(db, captureInstance, inserted: 1));
            Assert.Equal(new[] { 1 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustSwitchAsAMemberOfDbOwnerWithoutAnyServerPermission()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await CreateServiceBrokerTableAsync(db);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            var login = $"owner_{_table}";
            await db.ExecuteAsync($"""
                CREATE LOGIN [{login}] WITH PASSWORD = N'Owner!Passw0rd1', CHECK_POLICY = OFF;
                CREATE USER [{login}] FOR LOGIN [{login}];
                ALTER ROLE db_owner ADD MEMBER [{login}];
                """);
            var connectionString = new SqlConnectionStringBuilder(db.ConnectionString) { UserID = login, Password = "Owner!Passw0rd1" }.ConnectionString;
            await InsertAsync(feed, 1);

            try
            {
                Assert.Equal(0, await db.ScalarAsync<int>($"""
                    SELECT COUNT(*) FROM sys.server_permissions WHERE grantee_principal_id = SUSER_ID(N'{login}') AND permission_name <> N'CONNECT SQL';
                    """));
                Assert.Equal(0, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.server_role_members WHERE member_principal_id = SUSER_ID(N'{login}');"));
                await ExecuteAsync(connectionString, SwitchScripts.RenderToCdc("dbo", _table, captureInstance, SourceName, timeoutSeconds: ScriptTimeoutSeconds), ScriptTimeoutSeconds);
                await InsertAsync(feed, 2);

                Assert.Equal(new[] { 2 }, await RelayWithCdcAsync(db, captureInstance, inserted: 2));
                Assert.Equal(new[] { 1 }, receiver.RowIds);
            }
            finally
            {
                SqlConnection.ClearPool(new SqlConnection(connectionString));
            }
        }

        [Fact]
        public async Task MustSwitchATableWhoseNamesNeedQuoting()
        {
            const string relaySchema = "relay ]'s";
            const string schema = "sw ]'s";
            const string sourceName = "src ]'s";
            var table = $"{_table} ]'t";
            await using var db = await _sql.CreateDatabaseAsync(relaySchema);
            var captureInstance = await db.CreateCapturedTableAsync(table, schema, captureInstance: $"ci ]'{_table}");
            var feed = await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, table, schema);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await InsertAsync(feed, 1);

            await ExecuteAsync(db.ConnectionString, SwitchScripts.RenderToCdc(schema, table, captureInstance, sourceName, relaySchema, timeoutSeconds: ScriptTimeoutSeconds), ScriptTimeoutSeconds);
            await InsertAsync(feed, 2);

            Assert.Equal(new[] { 2 }, await RelayWithCdcAsync(db, captureInstance, inserted: 2, schema, table, sourceName));
            Assert.Equal(new[] { 1 }, receiver.RowIds);
            Assert.True(await feed.IsTriggerDisabledAsync());
        }

        [Fact]
        public void MustWaitForCaptureWithThePredicateTheProbesConfirm()
        {
            static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
            var script = Normalize(new SqlScriptFile("switch-to-cdc.sql").Text);

            Assert.Contains(Normalize($"DECLARE @t datetime2 = SYSDATETIME(); DECLARE @scanAtT int = {WhenRelyingOnSqlServerToSwitchSources.LatestLogScan};"), script);
            Assert.Contains(Normalize(WhenRelyingOnSqlServerToSwitchSources.CaptureReadPast), script);
        }

        private async Task<(string CaptureInstance, ServiceBrokerChangeFeed Feed)> CreateServiceBrokerTableAsync(SqlServerFixture.TestDatabase db)
        {
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            return (captureInstance, await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table));
        }

        private Task SwitchAsync(SqlServerFixture.TestDatabase db, string captureInstance, bool keepDeadLetters = false, int timeoutSeconds = ScriptTimeoutSeconds)
            => ExecuteAsync(db.ConnectionString, SwitchScripts.RenderToCdc("dbo", _table, captureInstance, SourceName, keepDeadLetters: keepDeadLetters, timeoutSeconds: timeoutSeconds), timeoutSeconds);

        /// <summary>
        /// Relays the capture instance with CDC, as source <paramref name="sourceName"/>, from its checkpoint until it's caught up,
        /// once CDC has captured <paramref name="inserted"/> inserts, and returns the ids of the rows it relayed.
        /// </summary>
        private async Task<IReadOnlyList<int>> RelayWithCdcAsync(SqlServerFixture.TestDatabase db, string captureInstance, int inserted, string schema = "dbo", string table = null, string sourceName = SourceName)
        {
            await WaitUntilAsync(async () => await db.ScalarAsync<int>($"SELECT COUNT(*) FROM cdc.{SqlServerFixture.TestDatabase.Quote(captureInstance + "_CT")} WHERE __$operation = 2;") >= inserted);
            var processor = new Processor();
            var poller = CreatePoller(db, schema, table ?? _table, captureInstance, sourceName, processor);
            for (var polls = 0; await poller.PollOnceAsync(CancellationToken.None); polls++)
            {
                if (polls > 100)
                    throw new TimeoutException("CDC didn't catch up.");
            }
            return processor.RowIds;
        }

        private static CdcCapturePoller<Row> CreatePoller(SqlServerFixture.TestDatabase db, string schema, string table, string captureInstance, string sourceName, Processor processor)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IRowChangeProcessor<Row>>(processor);
            var options = new CdcSourceOptions();
            options.PollInterval = options.LeaseRetryInterval = options.RetryDelay = TimeSpan.FromMilliseconds(200);
            var capture = new CdcCapture(sourceName, captureInstance, new SourceTable(table, schema, db.Name), typeof(Row), options);
            return new CdcCapturePoller<Row>(capture, new SqlCdcQueries(db.ConnectionString), new SqlCheckpointStore(db.ConnectionString, db.RelaySchema),
                new SqlParkedChangeStore(db.ConnectionString, db.RelaySchema), new SqlAppLockLeaseFactory(db.ConnectionString),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<CdcCapturePoller<Row>>.Instance);
        }

        private static async Task AssertNothingChangedAsync(SqlServerFixture.TestDatabase db, ServiceBrokerChangeFeed feed, string captureInstance)
        {
            Assert.False(await feed.IsTriggerDisabledAsync(), "The switch disabled the trigger");
            Assert.Null(await CheckpointAsync(db, captureInstance));
        }

        private static Task<byte[]> CheckpointAsync(SqlServerFixture.TestDatabase db, string captureInstance)
            => db.ScalarAsync<byte[]>($"""
                SELECT LastLsn FROM {SqlServerFixture.TestDatabase.Quote(db.RelaySchema)}.Chatter_RelayCheckpoints
                WHERE CaptureInstance = N'{captureInstance.Replace("'", "''")}';
                """);

        /// <summary>
        /// Whether a lock on the whole table is held (<c>GRANT</c>) or waited for (<c>WAIT</c>) in shared mode, as the switch takes it.
        /// </summary>
        private static async Task<bool> CountTableLocksAsync(SqlServerFixture.TestDatabase db, ServiceBrokerChangeFeed feed, string status)
            => await db.ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE resource_database_id = DB_ID() AND resource_type = N'OBJECT' AND resource_associated_entity_id = OBJECT_ID(N'{feed.QualifiedTable.Replace("'", "''")}')
                    AND request_mode = N'S' AND request_status = N'{status}';
                """) > 0;

        private static async Task InsertAsync(ServiceBrokerChangeFeed feed, params int[] ids)
        {
            foreach (var id in ids)
                await ExecuteAsync(feed.ConnectionString, $"INSERT INTO {feed.QualifiedTable} (Id) VALUES ({id});", 30);
        }

        private static async Task WriteContinuouslyAsync(SqlServerFixture.TestDatabase db, string table, CancellationToken stop)
        {
            for (var id = 1; !stop.IsCancellationRequested; id++)
            {
                await db.ExecuteAsync($"INSERT INTO dbo.[{table}] (Id) VALUES ({id});");
                await Task.Delay(100, CancellationToken.None);
            }
        }

        private static async Task ExecuteAsync(string connectionString, string sql, int timeoutSeconds)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds + 60 };
            await command.ExecuteNonQueryAsync();
        }

        private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
        {
            await using var command = new SqlCommand(sql, connection);
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
                await Task.Delay(200);
            }
        }

        public class SwitchedEvent : IMessage { }

        public class OtherSwitchedEvent : IMessage { }

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool Flag { get; set; }
            public DateTime? DeletedAt { get; set; }
        }

        private sealed class Processor : IRowChangeProcessor<Row>
        {
            private readonly ConcurrentQueue<int> _rowIds = new();

            public IReadOnlyList<int> RowIds => _rowIds.ToList();

            public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
            {
                _rowIds.Enqueue(change.NewValue.Id);
                return Task.CompletedTask;
            }

            public async Task ProcessBatchAsync(IReadOnlyList<RowChange<Row>> changes, CancellationToken cancellationToken)
            {
                foreach (var change in changes)
                    await ProcessAsync(change, cancellationToken);
            }
        }
    }
}
