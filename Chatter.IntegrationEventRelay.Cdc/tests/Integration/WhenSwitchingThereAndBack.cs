using Chatter.CQRS;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// <c>.scripts/cdc/switch-to-cdc.sql</c> and <c>.scripts/cdc/switch-to-service-broker.sql</c> switch a table back and forth
    /// while it's written to: each change is relayed once, by the change source the table was on when the change committed,
    /// and never by both or neither.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", Category.Integration)]
    public class WhenSwitchingThereAndBack
    {
        private const string SourceName = "round-trip-cdc";
        private const int ScriptTimeoutSeconds = 90;
        // Writes the writer makes between two switches
        private const int WritesBetweenSwitches = 6;

        // CDC's capture job reads the log every few seconds
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Blocked = TimeSpan.FromSeconds(3);

        private readonly SqlServerFixture _sql;
        private readonly string _table = $"Switch_{Guid.NewGuid():N}"[..20];

        public WhenSwitchingThereAndBack(SqlServerFixture sql) => _sql = sql;

        [Theory]
        [InlineData(CdcErrorPolicy.Halt)]
        [InlineData(CdcErrorPolicy.ParkRow)]
        public async Task MustRelayEachChangeOnceByTheSourceTheTableWasOnWhenItCommitted(CdcErrorPolicy errorPolicy)
        {
            await using var db = await _sql.CreateDatabaseAsync();
            var captureInstance = await db.CreateCapturedTableAsync(_table);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            var toCdc = SwitchScripts.RenderToCdc("dbo", _table, captureInstance, SourceName, db.RelaySchema, timeoutSeconds: ScriptTimeoutSeconds);
            var toServiceBroker = SwitchScripts.RenderToServiceBroker("dbo", _table, captureInstance, db.RelaySchema, ScriptTimeoutSeconds);
            var written = new ConcurrentQueue<int>();
            using var stop = new CancellationTokenSource();
            var writer = WriteContinuouslyAsync(db, written, stop.Token);
            CdcRelay relay = null;

            try
            {
                try
                {
                    await WaitForMoreWritesAsync(written);
                    await SwitchWhileWritingAsync(db, toCdc);
                    // Started after the first switch, which seeds its checkpoint, and left running through the others
                    relay = CdcRelay.Start(db, captureInstance, _table, SourceName, errorPolicy);
                    await WaitForMoreWritesAsync(written);
                    await SwitchWhileWritingAsync(db, toServiceBroker);
                    await WaitForMoreWritesAsync(written);
                    await SwitchWhileWritingAsync(db, toCdc);
                    await WaitForMoreWritesAsync(written);
                }
                finally
                {
                    stop.Cancel();
                    await writer;
                }

                var expected = written.ToList();
                await WaitUntilAsync(() => relay.RowIds.Count + receiver.RowIds.Count >= expected.Count);
                await Task.Delay(Blocked);

                Assert.Equal(expected.OrderBy(id => id), relay.RowIds.Concat(receiver.RowIds).OrderBy(id => id));
                Assert.NotEmpty(relay.RowIds);
                Assert.NotEmpty(receiver.RowIds);
                Assert.Equal(0, await feed.CountQueueAsync());
                Assert.Equal(0, await feed.CountDeadLetterQueueAsync());
                Assert.Equal(0, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {SqlServerFixture.TestDatabase.Quote(db.RelaySchema)}.Chatter_RelayParkedChanges;"));
                Assert.True(await feed.IsTriggerDisabledAsync());
            }
            finally
            {
                if (relay is not null)
                    await relay.DisposeAsync();
            }
        }

        /// <summary>
        /// Runs a switch script while the writer goes on writing, and checks that a write waited for the switch.
        /// </summary>
        private async Task SwitchWhileWritingAsync(SqlServerFixture.TestDatabase db, string script)
        {
            var switching = ExecuteAsync(db.ConnectionString, script, ScriptTimeoutSeconds);
            var writeWaited = false;
            await WaitUntilAsync(async () => switching.IsCompleted || (writeWaited = await CountWaitingWritesAsync(db) > 0));
            await switching;
            Assert.True(writeWaited, "No write waited for the switch");
        }

        /// <summary>
        /// Inserts rows 1, 2, 3, ... until stopped, and with every third row updates the row before it and deletes the one
        /// before that. Records the id of the row each committed change wrote, as the relay's sources report it.
        /// </summary>
        private async Task WriteContinuouslyAsync(SqlServerFixture.TestDatabase db, ConcurrentQueue<int> written, CancellationToken stop)
        {
            for (var id = 1; !stop.IsCancellationRequested; id++)
            {
                await WriteAsync(db, written, $"INSERT INTO dbo.[{_table}] (Id, Name) VALUES ({id}, N'inserted');", id);
                if (id % 3 == 0)
                {
                    await WriteAsync(db, written, $"UPDATE dbo.[{_table}] SET Name = N'updated' WHERE Id = {id - 1};", id - 1);
                    await WriteAsync(db, written, $"DELETE FROM dbo.[{_table}] WHERE Id = {id - 2};", id - 2);
                }
                await Task.Delay(50, CancellationToken.None);
            }
        }

        private static async Task WriteAsync(SqlServerFixture.TestDatabase db, ConcurrentQueue<int> written, string sql, int rowId)
        {
            await db.ExecuteAsync(sql);
            written.Enqueue(rowId);
        }

        private static Task WaitForMoreWritesAsync(ConcurrentQueue<int> written)
        {
            var target = written.Count + WritesBetweenSwitches;
            return WaitUntilAsync(() => written.Count >= target);
        }

        /// <summary>
        /// Writes to the table waiting for the lock the switch scripts take on it.
        /// </summary>
        private async Task<int> CountWaitingWritesAsync(SqlServerFixture.TestDatabase db)
            => await db.ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE resource_database_id = DB_ID() AND resource_type = N'OBJECT' AND resource_associated_entity_id = OBJECT_ID(N'dbo.[{_table}]')
                    AND request_mode = N'IX' AND request_status = N'WAIT';
                """);

        private static async Task ExecuteAsync(string connectionString, string sql, int timeoutSeconds)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds + 60 };
            await command.ExecuteNonQueryAsync();
        }

        private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow + CaptureTimeout;
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("SQL Server or the relay didn't reach the expected state in time. Is SQL Server Agent running?");
                await Task.Delay(200);
            }
        }

        public class SwitchedEvent : IMessage { }
    }
}
