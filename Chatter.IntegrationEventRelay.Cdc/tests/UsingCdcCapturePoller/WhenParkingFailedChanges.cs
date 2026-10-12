using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Cdc.Tests.Fakes;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcCapturePoller
{
    public class WhenParkingFailedChanges
    {
        private readonly PollerHarness<Row> _harness = new(new CdcSourceOptions { ErrorPolicy = CdcErrorPolicy.ParkRow, MaxAttemptsBeforePark = 3 });
        private readonly Processor _processor = new();

        public WhenParkingFailedChanges() => _harness.Processor = _processor;

        private FakeCdcQueries Cdc => _harness.Queries;
        private FakeParkedChangeStore Parked => _harness.Parked;

        [Fact]
        public async Task MustRetryTheBatchUntilTheChangeHasFailedEnoughTimes()
        {
            Change(1, "a");
            Change(2, "poison");

            for (var attempt = 1; attempt < 3; attempt++)
                await Assert.ThrowsAsync<InvalidOperationException>(() => PollAsync());

            Assert.Empty(Parked.Changes);
            Assert.Null(_harness.Checkpoint);
        }

        [Fact]
        public async Task MustParkAChangeThatKeepsFailingAndCarryOnWithOtherRows()
        {
            Change(1, "a");
            Change(2, "poison");
            var last = Change(3, "c");

            await PollUntilSuccessAsync();

            var parked = Assert.Single(Parked.Changes);
            Assert.Equal("[2]", parked.RowKey);
            Assert.Equal(3, parked.Attempts);
            Assert.Contains("poison", parked.Error);
            Assert.Contains(3, _processor.Published.Select(c => c.NewValue.Id));
            Assert.Equal(last.Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustParkLaterChangesToTheSameRowBehindIt()
        {
            Change(2, "poison");
            await PollUntilSuccessAsync();
            Change(2, "fixed later");
            Change(1, "other row");

            await PollAsync();

            Assert.Equal(new[] { "poison", "fixed later" }, Parked.Changes.Select(c => Payload(c)));
            Assert.Equal(new[] { 1 }, _processor.Published.Select(c => c.NewValue.Id));
        }

        [Fact]
        public async Task MustParkLaterChangesInTheSameBatchBehindIt()
        {
            Cdc.Commit((CdcChangeRow.Insert, Columns(2, "poison")), (CdcChangeRow.Delete, Columns(2, "poison")), (CdcChangeRow.Insert, Columns(1, "a")));

            await PollUntilSuccessAsync();

            Assert.Equal(new[] { "Insert", "Delete" }, Parked.Changes.Select(c => c.Operation));
            Assert.Equal(0, Parked.Changes[1].Attempts);
        }

        [Fact]
        public async Task MustNeverParkTransientFailures()
        {
            Change(1, "transient");

            for (var attempt = 0; attempt < 10; attempt++)
                await Assert.ThrowsAsync<TimeoutException>(() => PollAsync());

            Assert.Empty(Parked.Changes);
            Assert.Null(_harness.Checkpoint);
        }

        [Fact]
        public async Task MustNotParkAChangeTwiceWhenItsBatchIsReplayed()
        {
            Change(2, "poison");
            await PollUntilSuccessAsync();
            _harness.Checkpoint = null;

            await PollAsync();

            Assert.Single(Parked.Changes);
        }

        [Fact]
        public async Task MustReplayARequestedRowInOrderWithTheOriginalPositions()
        {
            Change(2, "poison");
            await PollUntilSuccessAsync();
            Change(2, "next");
            await PollAsync();
            var positions = Parked.Changes.Select(c => c.Position).ToList();
            _processor.Poison = "nothing";

            Parked.RequestReplay("[2]");
            await PollAsync();

            Assert.Empty(Parked.Changes);
            Assert.Equal(new[] { "poison", "next" }, _processor.Published.Select(c => c.NewValue.Name));
            Assert.Equal(positions, _processor.Published.Select(c => c.Position.Value));
            Assert.All(_processor.Published, c => Assert.Equal(RowOperation.Update, c.Operation));
            Assert.Equal("before", _processor.Published[0].OldValue.Name);
        }

        [Fact]
        public async Task MustPublishChangesToAReplayedRowNormallyAfterwards()
        {
            Change(2, "poison");
            await PollUntilSuccessAsync();
            _processor.Poison = "nothing";
            Parked.RequestReplay("[2]");
            await PollAsync();

            Change(2, "after");
            await PollAsync();

            Assert.Equal(new[] { "poison", "after" }, _processor.Published.Select(c => c.NewValue.Name));
            Assert.Empty(Parked.Changes);
        }

        [Fact]
        public async Task MustKeepARowParkedIfItsReplayFailsAgain()
        {
            Change(2, "poison");
            await PollUntilSuccessAsync();

            Parked.RequestReplay("[2]");
            await PollAsync();

            var parked = Assert.Single(Parked.Changes);
            Assert.Equal(4, parked.Attempts);
            Assert.False(Parked.IsReplayRequested("[2]"));
            Assert.Empty(_processor.Published);
        }

        [Fact]
        public async Task MustRequireAKeyToIdentifyRows()
        {
            Cdc.KeyColumns.Clear();
            Change(1, "a");

            var e = await Assert.ThrowsAsync<InvalidOperationException>(() => PollAsync());

            Assert.Contains("unique index", e.Message);
        }

        [Fact]
        public async Task MustNotParkWithTheHaltPolicy()
        {
            var harness = new PollerHarness<Row> { Processor = _processor };
            harness.Queries.Commit((CdcChangeRow.Insert, Columns(2, "poison")));

            for (var attempt = 0; attempt < 5; attempt++)
                await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Poller.PollOnceAsync(CancellationToken.None));

            Assert.Empty(harness.Parked.Changes);
            Assert.Null(harness.Checkpoint);
        }

        private Task<bool> PollAsync() => _harness.Poller.PollOnceAsync(CancellationToken.None);

        private async Task PollUntilSuccessAsync()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    await PollAsync();
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            throw new Xunit.Sdk.XunitException("The batch never succeeded");
        }

        private FakeTransaction Change(int id, string name)
            => Cdc.Commit((CdcChangeRow.UpdateBefore, Columns(id, "before")), (CdcChangeRow.UpdateAfter, Columns(id, name)));

        private static IReadOnlyDictionary<string, object> Columns(int id, string name) => PollerHarness<Row>.Columns(id, name);

        private static string Payload(ParkedChange change) => System.Text.Json.JsonDocument.Parse(change.Payload).RootElement.GetProperty("NewValue").GetProperty("Name").GetString();

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>Fails on rows named "poison" (not transient) or "transient".</summary>
        private sealed class Processor : IRowChangeProcessor<Row>
        {
            public string Poison { get; set; } = "poison";
            public List<RowChange<Row>> Published { get; } = new();

            public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
            {
                if (change.NewValue.Name == Poison)
                    throw new InvalidOperationException("Mapper failed on poison");
                if (change.NewValue.Name == "transient")
                    throw new TimeoutException("Broker unavailable");
                Published.Add(change);
                return Task.CompletedTask;
            }
        }
    }
}
