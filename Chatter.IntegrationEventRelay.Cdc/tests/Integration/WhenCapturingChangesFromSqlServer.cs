using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", Category.Integration)]
    public class WhenCapturingChangesFromSqlServer
    {
        // CDC's capture job reads the log every few seconds
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(90);

        private readonly SqlServerFixture _sql;
        private readonly string _table = $"Event_{Guid.NewGuid():N}"[..20];
        private readonly Processor _processor = new();

        public WhenCapturingChangesFromSqlServer(SqlServerFixture sql) => _sql = sql;

        [Fact]
        public async Task MustDeliverInsertsUpdatesAndDeletesInCommitOrderWithOldValues()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name, Flag) VALUES (1, N'one', 1);");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'uno' WHERE Id = 1;");
            await _sql.ExecuteAsync($"DELETE FROM dbo.[{_table}] WHERE Id = 1;");

            var changes = await PollUntilAsync(poller, 3);

            Assert.Equal(new[] { RowOperation.Insert, RowOperation.Update, RowOperation.Delete }, changes.Select(c => c.Operation));
            Assert.Equal("one", changes[1].OldValue.Name);
            Assert.Equal("uno", changes[1].NewValue.Name);
            Assert.Equal("uno", changes[2].NewValue.Name);
            Assert.All(changes, c => Assert.Equal(_table, c.Table.Name));
            Assert.All(changes, c => Assert.NotNull(c.CommittedAt));
            Assert.True(changes.Select(c => c.Position.Order).SequenceEqual(changes.Select(c => c.Position.Order).OrderBy(o => o)));
        }

        [Fact]
        public async Task MustDeliverASoftDeleteThenRestoreInOrder()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one');");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET DeletedAt = SYSUTCDATETIME() WHERE Id = 1;");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET DeletedAt = NULL WHERE Id = 1;");

            var updates = (await PollUntilAsync(poller, 3)).Where(c => c.Operation == RowOperation.Update).ToList();

            Assert.Equal(2, updates.Count);
            Assert.True(updates[0].OldValue.DeletedAt is null && updates[0].NewValue.DeletedAt is not null, "first update deletes");
            Assert.True(updates[1].OldValue.DeletedAt is not null && updates[1].NewValue.DeletedAt is null, "second update restores");
        }

        [Fact]
        public async Task MustResumeAfterARestartWithoutLossAndWithTheSamePositions()
        {
            var processor = new Processor { FailOnId = 2 };
            var first = await CreatePollerAsync(processor);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one'), (2, N'two'), (3, N'three');");
            await WaitForCaptureAsync(3);

            await Assert.ThrowsAsync<InvalidOperationException>(() => first.PollOnceAsync(CancellationToken.None));
            var attempted = processor.Changes.ToList();
            Assert.Null(await Checkpoints().GetAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None));

            // A new relay instance, as after a crash
            var restarted = new Processor();
            var changes = await PollUntilAsync(await CreatePollerAsync(restarted, createTable: false), 3, restarted);

            Assert.Equal(new[] { 1, 2, 3 }, changes.Select(c => c.NewValue.Id));
            Assert.Equal(attempted.Single().Position, changes[0].Position);
            Assert.NotNull(await Checkpoints().GetAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustReadBatchesOfAtMostMaxTransactions()
        {
            var processor = new Processor();
            var poller = await CreatePollerAsync(processor, options: new CdcSourceOptions { MaxTransactionsPerBatch = 1 });
            for (var id = 1; id <= 3; id++)
                await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES ({id}, N'row');");
            await WaitForCaptureAsync(3);

            var batches = 0;
            while (processor.Changes.Count < 3 && batches < 10)
            {
                await poller.PollOnceAsync(CancellationToken.None);
                batches++;
            }

            Assert.Equal(new[] { 1, 2, 3 }, processor.Changes.Select(c => c.NewValue.Id));
            Assert.Equal(new[] { 1, 1, 1 }, processor.BatchSizes.Where(s => s > 0));
        }

        [Fact]
        public async Task MustUseTheDefaultForANullColumnOfANonNullableProperty()
        {
            var poller = await CreatePollerAsync();
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name, Flag) VALUES (1, NULL, NULL);");

            var change = Assert.Single(await PollUntilAsync(poller, 1));

            Assert.False(change.NewValue.Flag);
            Assert.Null(change.NewValue.Name);
        }

        [Fact]
        public async Task MustStartAfterTheLatestChangeIfConfigured()
        {
            var processor = new Processor();
            await _sql.CreateCapturedTableAsync(_table);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'old');");
            await WaitForCaptureAsync(1);

            var poller = await CreatePollerAsync(processor, createTable: false, options: new CdcSourceOptions { StartPosition = CdcStartPosition.Latest });
            await poller.PollOnceAsync(CancellationToken.None);
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (2, N'new');");

            var changes = await PollUntilAsync(poller, 1, processor);

            Assert.Equal(2, Assert.Single(changes).NewValue.Id);
        }

        [Fact]
        public async Task MustFailForAMissingCaptureInstance()
        {
            var queries = new SqlCdcQueries(_sql.ConnectionString);

            Assert.True((await queries.GetMinLsnAsync("dbo_DoesNotExist", CancellationToken.None)).IsZero);
        }

        [Fact]
        public async Task MustListTheCapturedColumns()
        {
            var captureInstance = await _sql.CreateCapturedTableAsync(_table);

            var columns = await new SqlCdcQueries(_sql.ConnectionString).GetCapturedColumnsAsync(captureInstance, CancellationToken.None);

            Assert.Equal(new[] { "Id", "Name", "Flag", "DeletedAt" }, columns);
            Assert.Equal(new[] { "Id" }, await new SqlCdcQueries(_sql.ConnectionString).GetKeyColumnsAsync(captureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustKeepCheckpointsInTheRelaySchema()
        {
            await _sql.CreateCapturedTableAsync(_table, schema: "relay_it");
            var store = new SqlCheckpointStore(_sql.ConnectionString, "relay_it");
            var lsn = CdcLsn.Parse("0x0000002A000001F00003");

            await store.SaveAsync("fakedb-cdc", "dbo_X", lsn, CancellationToken.None);
            await store.SaveAsync("fakedb-cdc", "dbo_X", lsn.Increment(), CancellationToken.None);

            Assert.Equal(lsn.Increment(), await store.GetAsync("fakedb-cdc", "dbo_X", CancellationToken.None));
            Assert.Equal(1, await _sql.ScalarAsync<int>("SELECT COUNT(*) FROM relay_it.Chatter_RelayCheckpoints WHERE CaptureInstance = N'dbo_X';"));
        }

        [Fact]
        public async Task MustParkAFailingRowAndReplayItOnRequest()
        {
            var processor = new Processor { FailOnId = 2 };
            var poller = await CreatePollerAsync(processor, options: new CdcSourceOptions { ErrorPolicy = CdcErrorPolicy.ParkRow, MaxAttemptsBeforePark = 1 });
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (1, N'one'), (2, N'two');");
            await _sql.ExecuteAsync($"UPDATE dbo.[{_table}] SET Name = N'deux' WHERE Id = 2;");
            await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES (3, N'three');");
            await WaitForCaptureAsync(5);

            await WaitUntilAsync(async () =>
            {
                await poller.PollOnceAsync(CancellationToken.None);
                return processor.Changes.Count >= 2;
            });

            Assert.Equal(new[] { 1, 3 }, processor.Changes.Select(c => c.NewValue.Id));
            var parked = new SqlParkedChangeStore(_sql.ConnectionString, "Chatter");
            var rows = await parked.GetParkedChangesAsync("fakedb-cdc", Capture().CaptureInstance, "[2]", CancellationToken.None);
            Assert.Equal(new[] { "Insert", "Update" }, rows.Select(r => r.Operation));
            Assert.Equal(2, (await parked.GetSummaryAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None)).Count);

            processor.FailOnId = null;
            await _sql.ExecuteAsync($"UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME() WHERE CaptureInstance = N'dbo_{_table}' AND RowKey = N'[2]';");
            await poller.PollOnceAsync(CancellationToken.None);

            Assert.Equal(new[] { 1, 3, 2, 2 }, processor.Changes.Select(c => c.NewValue.Id));
            Assert.Equal("deux", processor.Changes.Last().NewValue.Name);
            Assert.Equal(rows.Select(r => r.Position), processor.Changes.Skip(2).Select(c => c.Position.Value));
            Assert.Empty(await parked.GetParkedRowKeysAsync("fakedb-cdc", Capture().CaptureInstance, CancellationToken.None));
        }

        [Fact]
        public async Task MustLetOnlyOneRelayInstanceHoldTheLease()
        {
            var resource = $"it:{_table}";
            await using var first = new SqlAppLockLease(_sql.ConnectionString, resource);
            await using var second = new SqlAppLockLease(_sql.ConnectionString, resource);

            Assert.True(await first.TryAcquireAsync(CancellationToken.None));
            Assert.False(await second.TryAcquireAsync(CancellationToken.None));
            Assert.True(await first.IsHeldAsync(CancellationToken.None));
            Assert.False(await second.IsHeldAsync(CancellationToken.None));

            await first.DisposeAsync();

            Assert.True(await second.TryAcquireAsync(CancellationToken.None));
            Assert.True(await second.IsHeldAsync(CancellationToken.None));
        }

        [Fact]
        public async Task MustRunOnePollerAtATimeAcrossInstances()
        {
            var a = new Processor();
            var b = new Processor();
            var pollerA = await CreatePollerAsync(a);
            var pollerB = await CreatePollerAsync(b, createTable: false);
            using var cts = new CancellationTokenSource();
            var runs = new[] { pollerA.RunAsync(cts.Token), pollerB.RunAsync(cts.Token) };

            for (var id = 1; id <= 5; id++)
                await _sql.ExecuteAsync($"INSERT INTO dbo.[{_table}] (Id, Name) VALUES ({id}, N'row');");
            await WaitUntilAsync(() => a.Changes.Count + b.Changes.Count >= 5);
            cts.Cancel();
            await Task.WhenAll(runs);

            Assert.True(a.Changes.Count == 0 || b.Changes.Count == 0, "Both instances published");
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, a.Changes.Concat(b.Changes).Select(c => c.NewValue.Id));
            Assert.Contains(CdcPollerState.Standby, new[] { pollerA.State, pollerB.State });
        }

        private CdcCapture Capture(CdcSourceOptions options = null)
        {
            options ??= new CdcSourceOptions();
            options.PollInterval = options.LeaseRetryInterval = options.RetryDelay = TimeSpan.FromMilliseconds(200);
            return new("fakedb-cdc", $"dbo_{_table}", new SourceTable(_table, "dbo", "RelayIntegration"), typeof(Row), options);
        }

        private SqlCheckpointStore Checkpoints() => new(_sql.ConnectionString, "Chatter");

        private async Task<CdcCapturePoller<Row>> CreatePollerAsync(Processor processor = null, bool createTable = true, CdcSourceOptions options = null)
        {
            if (createTable)
                await _sql.CreateCapturedTableAsync(_table);

            var services = new ServiceCollection();
            services.AddSingleton<IRowChangeProcessor<Row>>(processor ?? _processor);

            return new CdcCapturePoller<Row>(Capture(options), new SqlCdcQueries(_sql.ConnectionString), Checkpoints(), new SqlParkedChangeStore(_sql.ConnectionString, "Chatter"), new SqlAppLockLeaseFactory(_sql.ConnectionString),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<CdcCapturePoller<Row>>.Instance);
        }

        private async Task<IReadOnlyList<RowChange<Row>>> PollUntilAsync(CdcCapturePoller<Row> poller, int count, Processor processor = null)
        {
            processor ??= _processor;
            await WaitUntilAsync(async () =>
            {
                await poller.PollOnceAsync(CancellationToken.None);
                return processor.Changes.Count >= count;
            });
            return processor.Changes.ToList();
        }

        private Task WaitForCaptureAsync(int rows)
            => WaitUntilAsync(async () => await _sql.ScalarAsync<int>($"SELECT COUNT(*) FROM cdc.[dbo_{_table}_CT];") >= rows);

        private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

        private static async Task WaitUntilAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTime.UtcNow + CaptureTimeout;
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("CDC didn't capture the changes in time. Is SQL Server Agent running?");
                await Task.Delay(500);
            }
        }

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool Flag { get; set; }
            public DateTime? DeletedAt { get; set; }
        }

        private sealed class Processor : IRowChangeProcessor<Row>
        {
            private readonly ConcurrentQueue<RowChange<Row>> _changes = new();

            public int? FailOnId { get; set; }
            public IReadOnlyCollection<RowChange<Row>> Changes => _changes.ToList();
            public ConcurrentQueue<int> BatchSizes { get; } = new();

            public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
            {
                if (change.NewValue.Id == FailOnId)
                    throw new InvalidOperationException("publish failed");
                _changes.Enqueue(change);
                return Task.CompletedTask;
            }

            public async Task ProcessBatchAsync(IReadOnlyList<RowChange<Row>> changes, CancellationToken cancellationToken)
            {
                BatchSizes.Enqueue(changes.Count);
                foreach (var change in changes)
                    await ProcessAsync(change, cancellationToken);
            }
        }
    }
}
