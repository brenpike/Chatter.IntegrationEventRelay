using Chatter.CQRS;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        // The writer updates row UpdatedRows + n and deletes row DeletedRows + n with its nth insert, for n up to SeededRows
        private const int UpdatedRows = 1_000_000;
        private const int DeletedRows = 2_000_000;
        private const int SeededRows = 3_000;

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
            // The rows the writer updates and deletes, written before either source relays the table. Each change the writer
            // makes is then to a row no other change touches, so a change relayed twice can't make up for one relayed by neither.
            await db.ExecuteAsync($"""
                WITH Seeded AS (SELECT TOP ({SeededRows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b)
                INSERT INTO dbo.[{_table}] (Id, Name)
                SELECT {UpdatedRows} + N, N'seeded' FROM Seeded UNION ALL SELECT {DeletedRows} + N, N'seeded' FROM Seeded;
                """);
            var feed = await ServiceBrokerChangeFeed.InstallAsync<SwitchedEvent>(db.ConnectionString, _table);
            await using var receiver = ServiceBrokerReceiver.Start(feed);
            var toCdc = SwitchScripts.RenderToCdc("dbo", _table, captureInstance, SourceName, db.RelaySchema, timeoutSeconds: ScriptTimeoutSeconds);
            var toServiceBroker = SwitchScripts.RenderToServiceBroker("dbo", _table, captureInstance, db.RelaySchema, ScriptTimeoutSeconds);
            var written = new ConcurrentQueue<Write>();
            var switches = new List<Switch>();
            using var stop = new CancellationTokenSource();
            var writer = WriteContinuouslyAsync(db, written, stop.Token);
            CdcRelay relay = null;

            try
            {
                try
                {
                    await WaitForMoreWritesAsync(written);
                    switches.Add(await SwitchWhileWritingAsync(db, toCdc, Source.Cdc));
                    // Started after the first switch, which seeds its checkpoint, and left running through the others
                    relay = CdcRelay.Start(db, captureInstance, _table, SourceName, errorPolicy);
                    await WaitForMoreWritesAsync(written);
                    switches.Add(await SwitchWhileWritingAsync(db, toServiceBroker, Source.ServiceBroker));
                    await WaitForMoreWritesAsync(written);
                    switches.Add(await SwitchWhileWritingAsync(db, toCdc, Source.Cdc));
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

                // Each change relayed once, by one source or the other
                Assert.Equal(expected.Select(write => write.RowId).OrderBy(id => id), relay.RowIds.Concat(receiver.RowIds).OrderBy(id => id));
                // ... the one the table was on when it committed. A write made while a switch ran may have committed before or
                // after it, but one made wholly between two switches is relayed by the source the earlier of them switched to
                // (SQL Service Broker before the first), and every stretch between them has such writes.
                var between = expected
                    .Select(write => (write.RowId, Stretch: StretchOf(write, switches)))
                    .Where(write => write.Stretch is not null)
                    .Select(write => (write.RowId, Stretch: write.Stretch.Value))
                    .ToList();
                Assert.Equal(Enumerable.Range(0, switches.Count + 1), between.Select(write => write.Stretch).Distinct().OrderBy(stretch => stretch));
                var relayedWithCdc = relay.RowIds.ToHashSet();
                Assert.Empty(between
                    .Where(write => relayedWithCdc.Contains(write.RowId) != (SourceOf(write.Stretch, switches) == Source.Cdc))
                    .Select(write => $"row {write.RowId}, written after {write.Stretch} switch(es)"));
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
        private async Task<Switch> SwitchWhileWritingAsync(SqlServerFixture.TestDatabase db, string script, Source to)
        {
            var started = Stopwatch.GetTimestamp();
            var switching = ExecuteAsync(db.ConnectionString, script, ScriptTimeoutSeconds);
            var writeWaited = false;
            await WaitUntilAsync(async () => switching.IsCompleted || (writeWaited = await CountWaitingWritesAsync(db) > 0));
            await switching;
            var ended = Stopwatch.GetTimestamp();
            Assert.True(writeWaited, "No write waited for the switch");
            return new Switch(to, started, ended);
        }

        /// <summary>
        /// Inserts rows 1, 2, 3, ... until stopped, and with every third row n updates seeded row <see cref="UpdatedRows"/> + n
        /// and deletes seeded row <see cref="DeletedRows"/> + n, so that each change is to a row of its own. Records each
        /// committed change by the id of its row, as the relay's sources report it.
        /// </summary>
        private async Task WriteContinuouslyAsync(SqlServerFixture.TestDatabase db, ConcurrentQueue<Write> written, CancellationToken stop)
        {
            for (var id = 1; !stop.IsCancellationRequested; id++)
            {
                if (id > SeededRows)
                    throw new InvalidOperationException("The writer ran out of seeded rows to update and delete.");
                await WriteAsync(db, written, $"INSERT INTO dbo.[{_table}] (Id, Name) VALUES ({id}, N'inserted');", id);
                if (id % 3 == 0)
                {
                    await WriteAsync(db, written, $"UPDATE dbo.[{_table}] SET Name = N'updated' WHERE Id = {UpdatedRows + id};", UpdatedRows + id);
                    await WriteAsync(db, written, $"DELETE FROM dbo.[{_table}] WHERE Id = {DeletedRows + id};", DeletedRows + id);
                }
                await Task.Delay(50, CancellationToken.None);
            }
        }

        private static async Task WriteAsync(SqlServerFixture.TestDatabase db, ConcurrentQueue<Write> written, string sql, int rowId)
        {
            var started = Stopwatch.GetTimestamp();
            await db.ExecuteAsync(sql);
            written.Enqueue(new Write(rowId, started, Stopwatch.GetTimestamp()));
        }

        private static Task WaitForMoreWritesAsync(ConcurrentQueue<Write> written)
        {
            var target = written.Count + WritesBetweenSwitches;
            return WaitUntilAsync(() => written.Count >= target);
        }

        /// <summary>
        /// How many switches had ended when <paramref name="write"/> started, or null if a switch ran while it did, so that it
        /// may have committed before or after that switch.
        /// </summary>
        private static int? StretchOf(Write write, IReadOnlyList<Switch> switches)
            => switches.Any(s => s.Started <= write.Committed && write.Started <= s.Ended)
                ? null
                : switches.Count(s => s.Ended < write.Started);

        /// <summary>
        /// The source the table is on after <paramref name="stretch"/> switches.
        /// </summary>
        private static Source SourceOf(int stretch, IReadOnlyList<Switch> switches)
            => stretch == 0 ? Source.ServiceBroker : switches[stretch - 1].To;

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

        private enum Source { ServiceBroker, Cdc }

        /// <summary>
        /// A committed change to row <paramref name="RowId"/>, sent at <paramref name="Started"/> and committed by
        /// <paramref name="Committed"/> (<see cref="Stopwatch"/> timestamps).
        /// </summary>
        private sealed record Write(int RowId, long Started, long Committed);

        /// <summary>
        /// A switch to <paramref name="To"/>, started at <paramref name="Started"/> and committed by <paramref name="Ended"/>.
        /// </summary>
        private sealed record Switch(Source To, long Started, long Ended);
    }
}
