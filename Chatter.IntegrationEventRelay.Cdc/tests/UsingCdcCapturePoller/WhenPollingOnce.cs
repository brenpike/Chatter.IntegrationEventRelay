using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Cdc.Tests.Fakes;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcCapturePoller
{
    public class WhenPollingOnce
    {
        private readonly PollerHarness<Row> _harness = new();
        private readonly Mock<IRowChangeProcessor<Row>> _processor = new();
        private readonly List<IReadOnlyList<RowChange<Row>>> _batches = new();

        public WhenPollingOnce()
        {
            _processor.Setup(p => p.ProcessBatchAsync(It.IsAny<IReadOnlyList<RowChange<Row>>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<RowChange<Row>>, CancellationToken>((changes, _) => _batches.Add(changes))
                .Returns(Task.CompletedTask);
            _harness.Processor = _processor.Object;
        }

        private FakeCdcQueries Cdc => _harness.Queries;

        [Fact]
        public async Task MustStartFromTheOldestChangeWithoutACheckpoint()
        {
            var first = Insert(1);
            var last = Insert(2);

            Assert.True(await PollAsync());

            Assert.Equal((Cdc.MinLsn, last.Lsn), Cdc.ChangeReads.Single());
            Assert.Equal(new[] { 1, 2 }, _batches.Single().Select(c => c.NewValue.Id));
            Assert.Equal(last.Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustResumeAfterTheCheckpoint()
        {
            var published = Insert(1);
            var next = Insert(2);
            _harness.Checkpoint = published.Lsn;

            await PollAsync();

            Assert.Equal((published.Lsn.Increment(), next.Lsn), Cdc.ChangeReads.Single());
            Assert.Equal(2, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustReadAtMostMaxTransactionsPerBatch()
        {
            _harness.Options.MaxTransactionsPerBatch = 2;
            var transactions = Enumerable.Range(1, 5).Select(Insert).ToList();

            while (await PollAsync()) { }

            Assert.Equal(new[] { 2, 2, 1 }, _batches.Select(b => b.Count));
            Assert.All(Cdc.BatchSizes, size => Assert.Equal(2, size));
            Assert.Equal(new[] { transactions[1].Lsn, transactions[3].Lsn, transactions[4].Lsn }, _harness.Checkpoints.Saves);
        }

        [Fact]
        public async Task MustProcessEachBatchInItsOwnScope()
        {
            _harness.Options.MaxTransactionsPerBatch = 1;
            Insert(1);
            Insert(2);

            while (await PollAsync()) { }

            Assert.Equal(2, _harness.Scopes);
        }

        [Fact]
        public async Task MustNotCheckpointABatchThatFails()
        {
            Insert(1);
            _processor.Setup(p => p.ProcessBatchAsync(It.IsAny<IReadOnlyList<RowChange<Row>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("publish failed"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => PollAsync());

            Assert.Null(_harness.Checkpoint);
            Assert.Empty(_harness.Checkpoints.Saves);
        }

        [Fact]
        public async Task MustCheckpointABatchWithNoChangesToTheTable()
        {
            var transaction = Cdc.Commit();

            Assert.True(await PollAsync());

            _processor.Verify(p => p.ProcessBatchAsync(It.IsAny<IReadOnlyList<RowChange<Row>>>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal(transaction.Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustDoNothingWithoutNewChanges()
        {
            var published = Insert(1);
            _harness.Checkpoint = published.Lsn;

            Assert.False(await PollAsync());

            Assert.Empty(Cdc.ChangeReads);
            Assert.Empty(_harness.Checkpoints.Saves);
        }

        [Fact]
        public async Task MustMoveTheCheckpointPastHeartbeatsWhileIdle()
        {
            var published = Insert(1);
            _harness.Checkpoint = published.Lsn;
            var heartbeat = Cdc.CommitHeartbeat();

            Assert.False(await PollAsync());

            Assert.Equal(heartbeat.Lsn, _harness.Checkpoint);
            Assert.Empty(Cdc.ChangeReads);
            _processor.Verify(p => p.ProcessBatchAsync(It.IsAny<IReadOnlyList<RowChange<Row>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task MustNotReportAGapWhenCleanupPassesACheckpointThatOnlyHeartbeatsFollow()
        {
            var published = Insert(1);
            _harness.Checkpoint = published.Lsn;
            Cdc.CommitHeartbeat();
            var last = Cdc.CommitHeartbeat();
            await PollAsync();

            // Cleanup moves the low watermark past the last real transaction
            Cdc.MinLsn = last.Lsn;
            Insert(2);

            Assert.True(await PollAsync());
            Assert.Equal(2, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustNotRewriteAnUnchangedCheckpointWhileIdle()
        {
            Insert(1);
            await PollAsync();
            var saves = _harness.Checkpoints.Saves.Count;

            await PollAsync();
            await PollAsync();

            Assert.Equal(saves, _harness.Checkpoints.Saves.Count);
        }

        [Fact]
        public async Task MustNeverMoveTheCheckpointBackwards()
        {
            Insert(1);
            var latest = Insert(2);
            await PollAsync();

            await _harness.Checkpoints.SaveAsync(PollerHarness<Row>.SourceName, PollerHarness<Row>.CaptureInstance, Cdc.Transactions[0].Lsn, CancellationToken.None);

            Assert.Equal(latest.Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustDoNothingBeforeAnythingIsCaptured()
        {
            Assert.False(await PollAsync());

            Assert.Empty(Cdc.ChangeReads);
            Assert.Null(_harness.Checkpoint);
        }

        [Fact]
        public async Task MustStartAfterTheLatestChangeWithoutACheckpointIfConfigured()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            var old = Insert(1);

            Assert.True(await PollAsync());
            var latest = Insert(2);
            await PollAsync();

            Assert.Equal(new[] { old.Lsn, latest.Lsn }, _harness.Checkpoints.Saves);
            Assert.Equal(2, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustRelayTheFirstChangeCapturedAfterStartingAtTheLatestChangeOfAnEmptyDatabase()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;

            Assert.False(await PollAsync());
            Assert.Equal(Cdc.MinLsn.Decrement(), _harness.Checkpoint);
            var first = Insert(1);
            await PollAsync();

            Assert.Equal(1, _batches.Single().Single().NewValue.Id);
            Assert.Equal(first.Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustRelayTheFirstChangeCapturedAfterStartingAtTheLatestChangeBeforeTheCaptureJobRan()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            var enabledAt = Cdc.MinLsn;
            Cdc.MinLsn = CdcLsn.Zero;

            Assert.False(await PollAsync());
            Cdc.MinLsn = enabledAt;
            Insert(1);
            await PollAsync();

            Assert.Equal(1, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustDetectCleanupPastTheStartAnchorOfAnEmptyCaptureInstance()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            await PollAsync();

            // The relay is down while changes are made and then removed by cleanup
            Insert(1);
            var kept = Insert(2);
            Cdc.MinLsn = kept.Lsn;

            await Assert.ThrowsAsync<CdcGapException>(() => PollAsync());
        }

        [Fact]
        public async Task MustAnchorAtZeroBeforeTheCaptureJobsFirstScan()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            Cdc.MinLsn = CdcLsn.Zero;

            await PollAsync();

            Assert.Equal(CdcLsn.Zero, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustTakeTheLatestBoundaryBeforeTheFirstPollsOtherQueries()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            var old = Insert(1);

            // A change captured while the poll checks the capture instance's columns
            Cdc.OnGetCapturedColumns = () => Insert(2);
            await PollAsync();
            Cdc.OnGetCapturedColumns = null;
            await PollAsync();

            Assert.Equal(old.Lsn, _harness.Checkpoints.Saves[0]);
            Assert.Equal(2, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustKeepTheStartAnchorAcrossRestarts()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            await PollAsync();
            Insert(1);

            // A new relay instance with the same checkpoint store
            var restarted = new PollerHarness<Row>(_harness.Options) { Processor = _processor.Object };
            restarted.Checkpoint = _harness.Checkpoint;
            restarted.Queries.Commit((CdcChangeRow.Insert, PollerHarness<Row>.Columns(1, "row 1")));
            await restarted.Poller.PollOnceAsync(CancellationToken.None);

            Assert.Equal(1, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustIgnoreTheStartPositionWithACheckpoint()
        {
            _harness.Options.StartPosition = CdcStartPosition.Latest;
            var published = Insert(1);
            Insert(2);
            _harness.Checkpoint = published.Lsn;

            await PollAsync();

            Assert.Equal(2, _batches.Single().Single().NewValue.Id);
        }

        [Fact]
        public async Task MustFailOnAGapBetweenTheCheckpointAndTheOldestChange()
        {
            var published = Insert(1);
            Insert(2);
            Insert(3);
            _harness.Checkpoint = published.Lsn;
            Cdc.MinLsn = Cdc.Transactions[2].Lsn;

            var e = await Assert.ThrowsAsync<CdcGapException>(() => PollAsync());

            Assert.Equal(published.Lsn, e.Checkpoint);
            Assert.Equal(Cdc.MinLsn, e.MinLsn);
            Assert.Empty(Cdc.ChangeReads);
            Assert.Equal(published.Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustNotReportAGapIfTheNextChangeIsTheOldest()
        {
            var published = Insert(1);
            Insert(2);
            _harness.Checkpoint = published.Lsn;
            Cdc.MinLsn = published.Lsn.Increment();

            Assert.True(await PollAsync());
        }

        [Fact]
        public async Task MustFailIfTheCaptureInstanceDoesNotExist()
        {
            Cdc.CaptureInstanceExists = false;

            await Assert.ThrowsAsync<CdcCaptureInstanceNotFoundException>(() => PollAsync());
        }

        [Fact]
        public async Task MustRefuseACaptureInstanceOfAnotherTable()
        {
            Cdc.CapturedTable = new("dbo", "Event1_Old");
            Insert(1);

            var e = await Assert.ThrowsAsync<InvalidOperationException>(() => PollAsync());

            Assert.Contains("dbo.Event1_Old", e.Message);
            Assert.Empty(Cdc.ChangeReads);
        }

        [Fact]
        public async Task MustAcceptTheConfiguredTableWhateverItsCase()
        {
            Cdc.CapturedTable = new("DBO", "event1");
            Insert(1);

            Assert.True(await PollAsync());
        }

        [Fact]
        public async Task MustWarnAboutAPartitionedTableThatAllowsPartitionSwitches()
        {
            Cdc.CapturedTable = new("dbo", "Event1", Partitioned: true, PartitionSwitchAllowed: true);

            await PollAsync();

            Assert.Contains(_harness.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("partition switching allowed"));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task MustNotWarnAboutPartitionSwitchesThatCantHappen(bool partitioned, bool switchAllowed)
        {
            Cdc.CapturedTable = new("dbo", "Event1", partitioned, switchAllowed);

            await PollAsync();

            Assert.DoesNotContain(_harness.Logger.Entries, e => e.Message.Contains("partition switching allowed"));
        }

        [Fact]
        public async Task MustFailLoudlyWhenTheLoginMayNotReadTheCaptureInstance()
        {
            Cdc.Authorized = false;
            Insert(1);

            var e = await Assert.ThrowsAsync<CdcCaptureInstanceAccessDeniedException>(() => PollAsync());

            Assert.Contains("SELECT on every column", e.Message);
            Assert.Empty(Cdc.ChangeReads);
        }

        [Fact]
        public async Task MustWaitForTheCaptureJobAfterCdcIsEnabled()
        {
            Cdc.MinLsn = CdcLsn.Zero;

            Assert.False(await PollAsync());

            Assert.Empty(Cdc.ChangeReads);
            Assert.Null(_harness.Checkpoint);
        }

        [Fact]
        public async Task MustWarnOnceAboutPropertiesNoColumnSets()
        {
            Cdc.CapturedColumns.Remove("Name");

            await PollAsync();
            await PollAsync();

            var warning = Assert.Single(_harness.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
            Assert.Contains("Name", warning.Message);
        }

        [Fact]
        public async Task MustPassTheCancellationTokenToTheProcessor()
        {
            Insert(1);
            using var cts = new CancellationTokenSource();

            await _harness.Poller.PollOnceAsync(cts.Token);

            _processor.Verify(p => p.ProcessBatchAsync(It.IsAny<IReadOnlyList<RowChange<Row>>>(), cts.Token));
        }

        private Task<bool> PollAsync() => _harness.Poller.PollOnceAsync(CancellationToken.None);

        private FakeTransaction Insert(int id) => Cdc.Commit((CdcChangeRow.Insert, PollerHarness<Row>.Columns(id, $"row {id}")));

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
