using Chatter.CQRS;
using Chatter.IntegrationEventRelay.Cdc.Diagnostics;
using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// <c>.scripts/cdc/switch-to-service-broker.sql</c> moves a table from the CDC change source back to SQL Service Broker:
    /// CDC relays every change committed before the switch, SQL Service Broker every change committed after it. Each test
    /// puts the table on CDC as switch-to-cdc.sql leaves it, without running it: SqlChangeFeed installed with its trigger
    /// disabled, and a CDC relay's checkpoint seeded.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", Category.Integration)]
    public class WhenSwitchingBackToServiceBroker
    {
        // CDC's capture job reads the log every few seconds
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Blocked = TimeSpan.FromSeconds(3);
        // CDC cleanup moves its low watermark down to the first LSN committed at the same time, so a test that cleans up to a
        // transaction commits it this long after the changes it keeps out of the cleanup
        private static readonly TimeSpan CommitTimesApart = TimeSpan.FromSeconds(1);
        private static readonly CdcLsn Sealed = CdcLsn.Parse("0xFFFFFFFFFFFFFFFFFFFE");
        private const string SourceName = "fakedb-cdc";

        private readonly SqlServerFixture _sql;
        private readonly string _table = $"Switch_{Guid.NewGuid():N}"[..20];

        public WhenSwitchingBackToServiceBroker(SqlServerFixture sql) => _sql = sql;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MustRelayEachChangeOnceByCdcBeforeTheSwitchAndByServiceBrokerAfterIt(bool busy)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            // Another captured table written throughout: the capture job always has something to read
            if (busy)
                await db.CreateCapturedTableAsync($"{_table}_busy");
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            using var stop = new CancellationTokenSource();
            var writer = busy ? WriteContinuouslyAsync(db, $"{_table}_busy", stop.Token) : Task.CompletedTask;

            try
            {
                await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one'), (2, N'two');");
                await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (3, N'three');");
                await db.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'uno' WHERE Id = 1;");

                await SwitchAsync(db, captureInstance);
                await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (4, N'four');");
                await db.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'dos' WHERE Id = 2;");
                await db.ExecuteAsync($"DELETE FROM dbo.[{_table}] WHERE Id = 3;");
                await WaitUntilAsync(() => receiver.RowIds.Count >= 3);
                await WaitForCaptureAsync(db, captureInstance, 7);
                await Task.Delay(Blocked);
            }
            finally
            {
                stop.Cancel();
                await writer;
            }

            Assert.Equal(new[] { 1, 2, 3, 1 }, relay.RowIds);
            Assert.Equal(new[] { 4, 2, 3 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustRelayByCdcAWriteOpenAtTheSwitchAndByServiceBrokerAWriteThatWaitedForIt()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);

            await using var open = new SqlConnection(db.ConnectionString);
            await open.OpenAsync();
            await using var transaction = (SqlTransaction)await open.BeginTransactionAsync();
            await using (var insert = new SqlCommand($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);", open, transaction))
                await insert.ExecuteNonQueryAsync();
            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(async () => await CountTableLocksAsync(db, "S", "WAIT") > 0);
            Assert.False(switching.IsCompleted, "The switch didn't wait for the open write");
            await transaction.CommitAsync();
            await WaitUntilAsync(async () => await CountTableLocksAsync(db, "S", "GRANT") > 0);
            var waited = db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
            await WaitUntilAsync(async () => await CountTableLocksAsync(db, "IX", "WAIT") > 0);

            await switching;
            await waited;
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1);
            await WaitForCaptureAsync(db, captureInstance, 2);
            await Task.Delay(Blocked);

            Assert.Equal(new[] { 1 }, relay.RowIds);
            Assert.Equal(new[] { 2 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustWaitForTheCaptureJobAndThenForTheCdcRelay()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await WaitUntilAsync(() => relay.RowIds.Count >= 1);
            await relay.StopAsync();
            await db.ExecuteAsync("EXEC sys.sp_cdc_stop_job @job_type = N'capture';");
            await Task.Delay(TimeSpan.FromSeconds(2));
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");

            var switching = SwitchAsync(db, captureInstance);
            Assert.NotSame(switching, await Task.WhenAny(switching, Task.Delay(TimeSpan.FromSeconds(10))));
            await db.ExecuteAsync("EXEC sys.sp_cdc_start_job @job_type = N'capture';");
            await WaitForCaptureAsync(db, captureInstance, 2);
            Assert.NotSame(switching, await Task.WhenAny(switching, Task.Delay(Blocked)));
            var read = db.ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{_table}];");
            Assert.Same(read, await Task.WhenAny(read, Task.Delay(Blocked)));
            Assert.Equal(2, await read);
            relay.Resume();
            await switching;
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (3);");
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1);

            Assert.Equal(new[] { 1, 2 }, relay.RowIds);
            Assert.Equal(new[] { 3 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustLeaveTheCdcRelayHealthyCaughtUpAndRelayingNothingMore()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");

            await SwitchAsync(db, captureInstance);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1);
            await WaitForCaptureAsync(db, captureInstance, 2);
            await Task.Delay(Blocked);
            var health = await new CdcHealthCheck(new[] { relay.Poller }, TimeProvider.System, TimeSpan.FromMinutes(5)).CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(Sealed, await new SqlCheckpointStore(db.ConnectionString, db.RelaySchema).GetAsync(SourceName, captureInstance, CancellationToken.None));
            Assert.Equal(new[] { 1 }, relay.RowIds);
            Assert.Equal(new[] { 2 }, receiver.RowIds);
            Assert.Equal(CdcPollerState.Active, relay.Poller.State);
            Assert.Equal(TimeSpan.Zero, relay.Poller.GetLag(DateTimeOffset.UtcNow));
            Assert.Equal(HealthStatus.Healthy, health.Status);
            Assert.Equal(0, await feed.CountQueueAsync());
        }

        [Fact]
        public async Task MustWaitForEverySourceOfTheCaptureInstanceAndSealThemAll()
        {
            await using var db = await _sql.CreateDatabaseAsync(relaySchema: "Re]lay's");
            var (captureInstance, feed) = await PutOnCdcAsync(db, "first-cdc", "second-cdc");
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var first = CdcRelay.Start(db, captureInstance, _table, "first-cdc");
            await using var second = CdcRelay.Start(db, captureInstance, _table, "second-cdc");
            await second.StopAsync();
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");

            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(() => first.RowIds.Count >= 1);
            Assert.NotSame(switching, await Task.WhenAny(switching, Task.Delay(Blocked)));
            second.Resume();
            await switching;
            var checkpoints = new SqlCheckpointStore(db.ConnectionString, db.RelaySchema);

            Assert.Equal(new[] { 1 }, first.RowIds);
            Assert.Equal(new[] { 1 }, second.RowIds);
            Assert.Equal(Sealed, await checkpoints.GetAsync("first-cdc", captureInstance, CancellationToken.None));
            Assert.Equal(Sealed, await checkpoints.GetAsync("second-cdc", captureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustEnableEveryChangeFeedOfTheTable()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            var other = await ServiceBrokerChangeFeed.InstallAsync<OtherEvent>(db.ConnectionString, _table);
            await db.ExecuteAsync($"DISABLE TRIGGER {other.QualifiedTrigger} ON {other.QualifiedTable};");
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var otherReceiver = ServiceBrokerReceiver.Start(other);

            await SwitchAsync(db, captureInstance);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1 && otherReceiver.RowIds.Count >= 1);

            Assert.Equal(new[] { 1 }, receiver.RowIds);
            Assert.Equal(new[] { 1 }, otherReceiver.RowIds);
        }

        [Fact]
        public async Task MustRefuseWhileAChangeIsParkedAndSwitchOnceItIsReplayed()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName, CdcErrorPolicy.ParkRow, failOnId: 2);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1), (2);");
            await WaitUntilAsync(async () => await CountParkedAsync(db, captureInstance) == 1 && relay.RowIds.Count == 1);

            var e = await AssertRefusedAsync(db, captureInstance);
            Assert.Contains("parked", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);

            relay.FailOnId = null;
            await db.ExecuteAsync($"UPDATE {Quote(db.RelaySchema)}.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME() WHERE CaptureInstance = N'{captureInstance}';");
            await WaitUntilAsync(async () => await CountParkedAsync(db, captureInstance) == 0);
            await SwitchAsync(db, captureInstance);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (3);");
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1);

            Assert.Equal(new[] { 1, 2 }, relay.RowIds);
            Assert.Equal(new[] { 3 }, receiver.RowIds);
        }

        [Fact]
        public async Task MustRollBackWhenAChangeIsParkedWhileItWaits()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName, CdcErrorPolicy.ParkRow, failOnId: 2);
            await relay.StopAsync();
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1), (2);");

            var switching = SwitchAsync(db, captureInstance);
            await WaitUntilAsync(async () => await CountTableLocksAsync(db, "S", "GRANT") > 0);
            relay.Resume();
            var e = await Assert.ThrowsAsync<SqlException>(() => switching);

            Assert.Equal(50000, e.Number);
            Assert.Contains("parked", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (3);");
            await WaitUntilAsync(() => relay.RowIds.Count >= 2);
            Assert.Equal(new[] { 1, 3 }, relay.RowIds);
            Assert.Empty(receiver.RowIds);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MustRefuseWhenCdcCleanupPassedACheckpoint(bool emptied)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            var otherCaptureInstance = emptied ? await db.CreateCapturedTableAsync($"{_table}_other") : null;
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await Task.Delay(CommitTimesApart);
            if (emptied)
            {
                await db.ExecuteAsync($"INSERT INTO dbo.[{_table}_other] (Id) VALUES (1);");
                await WaitForCaptureAsync(db, captureInstance, 1);
                await WaitForCaptureAsync(db, otherCaptureInstance, 1);
                await CleanUpAsync(db, captureInstance, "sys.fn_cdc_get_max_lsn()");
            }
            else
            {
                await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
                await WaitForCaptureAsync(db, captureInstance, 2);
                await CleanUpAsync(db, captureInstance, $"(SELECT MAX(__$start_lsn) FROM cdc.[{captureInstance}_CT])");
            }
            Assert.Equal(emptied ? 0 : 1, await CountChangeTableRowsAsync(db, captureInstance));

            var e = await AssertRefusedAsync(db, captureInstance, timeoutSeconds: 30);

            Assert.Contains("on a gap", e.Message);
            Assert.EndsWith("Nothing was changed.", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
        }

        [Fact]
        public async Task MustRollBackWhenCdcCleanupPassesACheckpointWhileItWaits()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await Task.Delay(CommitTimesApart);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
            await WaitForCaptureAsync(db, captureInstance, 2);

            var switching = SwitchAsync(db, captureInstance, timeoutSeconds: 60);
            await WaitUntilAsync(async () => await CountTableLocksAsync(db, "S", "GRANT") > 0);
            Assert.False(switching.IsCompleted, "The switch didn't wait for the CDC relay");
            await CleanUpAsync(db, captureInstance, $"(SELECT MAX(__$start_lsn) FROM cdc.[{captureInstance}_CT])");
            Assert.Equal(1, await CountChangeTableRowsAsync(db, captureInstance));
            var e = await Assert.ThrowsAsync<SqlException>(() => switching);

            Assert.Equal(50000, e.Number);
            Assert.Contains("on a gap", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
        }

        [Fact]
        public async Task MustRollBackWhenTheCdcRelayDoesntCatchUpInTime()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName, failOnId: 2);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");

            var e = await AssertRefusedAsync(db, captureInstance, timeoutSeconds: 30);

            Assert.Contains("didn't relay", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
            relay.FailOnId = null;
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (3);");
            await WaitUntilAsync(() => relay.RowIds.Count >= 3);
            Assert.Equal(new[] { 1, 2, 3 }, relay.RowIds);
            Assert.Empty(receiver.RowIds);
        }

        [Fact]
        public async Task MustRollBackWhenATransactionOpenAtTheSwitchDoesNotFinishInTime()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            await using var open = new SqlConnection(db.ConnectionString);
            await open.OpenAsync();
            await using var transaction = (SqlTransaction)await open.BeginTransactionAsync();
            await using (var insert = new SqlCommand($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);", open, transaction))
                await insert.ExecuteNonQueryAsync();

            var e = await AssertRefusedAsync(db, captureInstance, timeoutSeconds: 5);
            await transaction.CommitAsync();

            Assert.Contains("couldn't lock the table", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
            await WaitUntilAsync(() => relay.RowIds.Count >= 1);
            Assert.Equal(new[] { 1 }, relay.RowIds);
            Assert.Empty(receiver.RowIds);
            Assert.Equal(0, await feed.CountQueueAsync());
        }

        [Fact]
        public async Task MustRollBackWhenCancelled()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            await relay.StopAsync();
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            using var cancel = new CancellationTokenSource();
            await using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(SwitchScripts.RenderToServiceBroker("dbo", _table, captureInstance, db.RelaySchema), connection);

            var switching = command.ExecuteNonQueryAsync(cancel.Token);
            await WaitUntilAsync(async () => await CountTableLocksAsync(db, "S", "GRANT") > 0);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<Exception>(() => switching);

            await using (var trancount = new SqlCommand("SELECT @@TRANCOUNT;", connection))
                Assert.Equal(0, (int)await trancount.ExecuteScalarAsync());
            await AssertOnCdcAsync(db, captureInstance, feed);
            var writing = db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
            Assert.Same(writing, await Task.WhenAny(writing, Task.Delay(Blocked)));
            await writing;
            relay.Resume();
            await WaitUntilAsync(() => relay.RowIds.Count >= 2);
            Assert.Equal(new[] { 1, 2 }, relay.RowIds);
            Assert.Empty(receiver.RowIds);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(86401)]
        public async Task MustRefuseATimeoutOutOfRange(int timeoutSeconds)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);

            var e = await AssertRefusedAsync(db, captureInstance, timeoutSeconds);

            Assert.Contains("@timeoutSeconds must be between 1 and 86400", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
        }

        [Fact]
        public async Task MustRefreshATriggerThatReadsAColumnDroppedWhileOnCdc()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            await db.ExecuteAsync($"ALTER TABLE dbo.[{_table}] DROP COLUMN Name;");
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");

            await SwitchAsync(db, captureInstance);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1);

            Assert.False(await feed.IsTriggerDisabledAsync());
            Assert.DoesNotContain("[Name]", await TriggerDefinitionAsync(db, feed));
            Assert.Equal(new[] { 1 }, relay.RowIds);
            Assert.Equal(new[] { 2 }, receiver.RowIds);
        }

        [Theory]
        [InlineData("INSERT, UPDATE, DELETE")]
        [InlineData("INSERT, UPDATE")]
        [InlineData("INSERT")]
        public async Task MustRefuseATriggerThatNoLongerCompilesWhenItsInstallProcedureIsGone(string events)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            var definition = await TriggerDefinitionAsync(db, feed);
            Assert.Contains("AFTER INSERT, UPDATE, DELETE", definition);
            await db.ExecuteAsync($"DROP TRIGGER {feed.QualifiedTrigger};");
            await db.ExecuteAsync(definition.Replace("AFTER INSERT, UPDATE, DELETE", $"AFTER {events}", StringComparison.Ordinal));
            await db.ExecuteAsync($"DISABLE TRIGGER {feed.QualifiedTrigger} ON {feed.QualifiedTable};");
            await feed.DropProceduresAsync();
            await db.ExecuteAsync($"ALTER TABLE dbo.[{_table}] DROP COLUMN Name;");

            var e = await AssertRefusedAsync(db, captureInstance);

            Assert.Contains("Invalid column name 'Name'", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");
            Assert.Equal(0, await feed.CountQueueAsync());
        }

        [Theory]
        [InlineData("DELETE")]
        [InlineData("INSERT, UPDATE, DELETE")]
        public async Task MustFireATriggerBehindAnInsteadOfTriggerToCheckIt(string events)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            var insteadOf = $"dbo.[{_table}_InsteadOf]";
            await db.ExecuteAsync($"CREATE TRIGGER {insteadOf} ON {feed.QualifiedTable} INSTEAD OF {events} AS SET NOCOUNT ON;");
            await feed.DropProceduresAsync();
            await db.ExecuteAsync($"ALTER TABLE dbo.[{_table}] DROP COLUMN Name;");

            var e = await AssertRefusedAsync(db, captureInstance);

            Assert.Contains("Invalid column name 'Name'", e.Message);
            await AssertOnCdcAsync(db, captureInstance, feed);
            Assert.False(await IsTriggerDisabledAsync(db, insteadOf), "The INSTEAD OF trigger was left disabled");
        }

        [Fact]
        public async Task MustLeaveTheTablesInsteadOfTriggersEnabled()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            var insteadOf = $"dbo.[{_table}_InsteadOf]";
            await db.ExecuteAsync($"CREATE TRIGGER {insteadOf} ON {feed.QualifiedTable} INSTEAD OF DELETE AS SET NOCOUNT ON;");

            await SwitchAsync(db, captureInstance);

            Assert.False(await IsTriggerDisabledAsync(db, insteadOf), "The INSTEAD OF trigger was left disabled");
            Assert.False(await feed.IsTriggerDisabledAsync(), "The trigger wasn't enabled");
        }

        [Fact]
        public async Task MustRefuseATableThatIsntOnCdc()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            await SeedCheckpointAsync(db, captureInstance, SourceName);

            var e = await AssertRefusedAsync(db, captureInstance);
            Assert.Contains("no SqlChangeFeed trigger", e.Message);

            var feed = await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table);
            e = await AssertRefusedAsync(db, captureInstance);
            Assert.Contains("is enabled", e.Message);
            Assert.False(await feed.IsTriggerDisabledAsync());
        }

        [Fact]
        public async Task MustRefuseParametersThatDontNameTheTablesCdcSource()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            var otherCaptureInstance = await db.CreateCapturedTableAsync($"{_table}_other");

            foreach (var (table, instance, relaySchema) in new[]
            {
                ($"{_table}_missing", captureInstance, db.RelaySchema),
                (_table, "dbo_Missing", db.RelaySchema),
                (_table, otherCaptureInstance, db.RelaySchema),
                (_table, captureInstance, "Missing")
            })
            {
                await AssertRefusedAsync(db, instance, table: table, relaySchema: relaySchema);
            }

            await db.ExecuteAsync($"DELETE FROM {Quote(db.RelaySchema)}.Chatter_RelayCheckpoints;");
            var e = await AssertRefusedAsync(db, captureInstance);
            Assert.Contains("No CDC source has a checkpoint", e.Message);
            await SeedCheckpointAsync(db, captureInstance, SourceName);

            await using (var connection = new SqlConnection(db.ConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await using var command = new SqlCommand(SwitchScripts.RenderToServiceBroker("dbo", _table, captureInstance, db.RelaySchema), connection, transaction);
                e = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
                Assert.Equal(50000, e.Number);
                Assert.Contains("outside a transaction", e.Message);
            }

            await AssertOnCdcAsync(db, captureInstance, feed);
        }

        [Fact]
        public async Task MustRefuseWhenTheTriggersCouldntSend()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);

            await db.ExecuteAsync($"ALTER QUEUE {feed.QualifiedQueue} WITH STATUS = OFF;");
            var e = await AssertRefusedAsync(db, captureInstance);
            Assert.Contains(feed.Service, e.Message);
            await db.ExecuteAsync($"ALTER QUEUE {feed.QualifiedQueue} WITH STATUS = ON;");

            await db.ExecuteAsync($"ALTER DATABASE [{db.Name}] SET DISABLE_BROKER WITH ROLLBACK IMMEDIATE;");
            SqlConnection.ClearPool(new SqlConnection(db.ConnectionString));
            e = await AssertRefusedAsync(db, captureInstance);
            Assert.Contains("SQL Service Broker is disabled", e.Message);

            await AssertOnCdcAsync(db, captureInstance, feed);
        }

        [Fact]
        public async Task MustSwitchAsADatabaseOwnerWithNoServerPermission()
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var (captureInstance, feed) = await PutOnCdcAsync(db, SourceName);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            await using var relay = CdcRelay.Start(db, captureInstance, _table, SourceName);
            var login = $"owner_{_table}";
            await _sql.ExecuteAsync($"""
                CREATE LOGIN [{login}] WITH PASSWORD = N'Owner!Passw0rd1', CHECK_POLICY = OFF;
                EXEC master.sys.sp_executesql N'DENY VIEW ANY DATABASE TO [{login}];';
                """);
            await db.ExecuteAsync($"CREATE USER [{login}] FOR LOGIN [{login}]; ALTER ROLE db_owner ADD MEMBER [{login}];");
            var owner = new SqlConnectionStringBuilder(db.ConnectionString) { UserID = login, Password = "Owner!Passw0rd1" }.ConnectionString;
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (1);");

            await SwitchAsync(db, captureInstance, connectionString: owner);
            await db.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id) VALUES (2);");
            await WaitUntilAsync(() => receiver.RowIds.Count >= 1);

            Assert.Equal(0, await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.server_permissions WHERE grantee_principal_id = SUSER_ID(N'{login}') AND state IN ('G', 'W') AND permission_name <> N'CONNECT SQL';"));
            Assert.Equal(0, await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM sys.server_role_members WHERE member_principal_id = SUSER_ID(N'{login}');"));
            Assert.Equal(new[] { 1 }, relay.RowIds);
            Assert.Equal(new[] { 2 }, receiver.RowIds);
            SqlConnection.ClearPool(new SqlConnection(owner));
        }

        /// <summary>
        /// Puts the table on CDC as switch-to-cdc.sql leaves it: creates it, enables CDC on it and installs SqlChangeFeed's
        /// change feed of <see cref="SwitchedEvent"/> on it with its trigger disabled, and seeds a checkpoint for each source.
        /// </summary>
        private async Task<(string CaptureInstance, ServiceBrokerChangeFeed Feed)> PutOnCdcAsync(SqlServerFixture.TestDatabase db, params string[] sourceNames)
        {
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table);
            await db.ExecuteAsync($"DISABLE TRIGGER {feed.QualifiedTrigger} ON {feed.QualifiedTable};");
            foreach (var sourceName in sourceNames)
                await SeedCheckpointAsync(db, captureInstance, sourceName);
            return (captureInstance, feed);
        }

        /// <summary>
        /// Seeds the checkpoint of <paramref name="sourceName"/> as switch-to-cdc.sql does, so CDC relays the changes committed after now.
        /// </summary>
        private static Task SeedCheckpointAsync(SqlServerFixture.TestDatabase db, string captureInstance, string sourceName)
            => db.ExecuteAsync($"""
                INSERT INTO {Quote(db.RelaySchema)}.Chatter_RelayCheckpoints (SourceName, CaptureInstance, LastLsn, UpdatedAt)
                SELECT {SqlScriptFile.Literal(sourceName)}, {SqlScriptFile.Literal(captureInstance)}, MAX(lsn), SYSUTCDATETIME()
                FROM (VALUES (sys.fn_cdc_get_max_lsn()), (sys.fn_cdc_decrement_lsn(sys.fn_cdc_get_min_lsn({SqlScriptFile.Literal(captureInstance)})))) AS seed (lsn);
                """);

        private Task SwitchAsync(SqlServerFixture.TestDatabase db, string captureInstance, int timeoutSeconds = 300, string table = null, string relaySchema = null, string connectionString = null)
            => SwitchAsync(connectionString ?? db.ConnectionString, SwitchScripts.RenderToServiceBroker("dbo", table ?? _table, captureInstance, relaySchema ?? db.RelaySchema, timeoutSeconds), timeoutSeconds);

        private static async Task SwitchAsync(string connectionString, string script, int timeoutSeconds)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(script, connection) { CommandTimeout = timeoutSeconds + 60 };
            await command.ExecuteNonQueryAsync();
        }

        private async Task<SqlException> AssertRefusedAsync(SqlServerFixture.TestDatabase db, string captureInstance, int timeoutSeconds = 300, string table = null, string relaySchema = null)
        {
            var e = await Assert.ThrowsAsync<SqlException>(() => SwitchAsync(db, captureInstance, timeoutSeconds, table, relaySchema));
            Assert.Equal(50000, e.Number);
            return e;
        }

        /// <summary>
        /// The table is still on CDC: its trigger disabled and no checkpoint of the capture instance sealed.
        /// </summary>
        private static async Task AssertOnCdcAsync(SqlServerFixture.TestDatabase db, string captureInstance, ServiceBrokerChangeFeed feed)
        {
            Assert.True(await feed.IsTriggerDisabledAsync(), "The trigger was enabled");
            Assert.Equal(0, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {Quote(db.RelaySchema)}.Chatter_RelayCheckpoints WHERE CaptureInstance = N'{captureInstance}' AND LastLsn = 0xFFFFFFFFFFFFFFFFFFFE;"));
        }

        /// <summary>
        /// Locks of mode <paramref name="mode"/> and status <paramref name="status"/> on the table, as the switch script and writers take them.
        /// </summary>
        private Task<int> CountTableLocksAsync(SqlServerFixture.TestDatabase db, string mode, string status)
            => db.ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE resource_database_id = DB_ID() AND resource_type = N'OBJECT' AND resource_associated_entity_id = OBJECT_ID(N'dbo.[{_table}]')
                    AND request_mode = N'{mode}' AND request_status = N'{status}';
                """);

        private static Task<int> CountParkedAsync(SqlServerFixture.TestDatabase db, string captureInstance)
            => db.ScalarAsync<int>($"SELECT COUNT(*) FROM {Quote(db.RelaySchema)}.Chatter_RelayParkedChanges WHERE CaptureInstance = N'{captureInstance}';");

        private static Task<bool> IsTriggerDisabledAsync(SqlServerFixture.TestDatabase db, string trigger)
            => db.ScalarAsync<bool>($"SELECT is_disabled FROM sys.triggers WHERE object_id = OBJECT_ID(N'{trigger.Replace("'", "''")}');");

        /// <summary>
        /// Runs CDC cleanup on the capture instance up to <paramref name="lowWaterMark"/>, a SQL expression for an LSN in
        /// <c>cdc.lsn_time_mapping</c>: it removes the changes committed before it.
        /// </summary>
        private static Task CleanUpAsync(SqlServerFixture.TestDatabase db, string captureInstance, string lowWaterMark)
            => db.ExecuteAsync($"""
                DECLARE @lowWaterMark binary(10) = {lowWaterMark};
                EXEC sys.sp_cdc_cleanup_change_table @capture_instance = {SqlScriptFile.Literal(captureInstance)}, @low_water_mark = @lowWaterMark, @threshold = 5000;
                """);

        private static Task<int> CountChangeTableRowsAsync(SqlServerFixture.TestDatabase db, string captureInstance)
            => db.ScalarAsync<int>($"SELECT COUNT(*) FROM cdc.[{captureInstance}_CT];");

        private static Task<string> TriggerDefinitionAsync(SqlServerFixture.TestDatabase db, ServiceBrokerChangeFeed feed)
            => db.ScalarAsync<string>($"SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'{feed.QualifiedTrigger}');");

        /// <summary>
        /// Waits until CDC has captured <paramref name="changes"/> changes of the table (an update's before and after image counted once).
        /// </summary>
        private static Task WaitForCaptureAsync(SqlServerFixture.TestDatabase db, string captureInstance, int changes)
            => WaitUntilAsync(async () => await db.ScalarAsync<int>($"SELECT COUNT(*) FROM cdc.[{captureInstance}_CT] WHERE __$operation <> 3;") >= changes);

        private static async Task WriteContinuouslyAsync(SqlServerFixture.TestDatabase db, string table, CancellationToken stop)
        {
            for (var id = 1; !stop.IsCancellationRequested; id++)
            {
                await db.ExecuteAsync($"INSERT INTO dbo.[{table}] (Id) VALUES ({id});");
                await Task.Delay(100, CancellationToken.None);
            }
        }

        private static string Quote(string name) => SqlServerFixture.TestDatabase.Quote(name);

        private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow + CaptureTimeout;
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("SQL Server or the relay didn't reach the expected state in time. Is SQL Server Agent running?");
                await Task.Delay(500);
            }
        }

        public class SwitchedEvent : IMessage { }

        public class OtherEvent : IMessage { }
    }
}
