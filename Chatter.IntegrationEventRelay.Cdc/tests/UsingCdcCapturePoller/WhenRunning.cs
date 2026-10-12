using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Cdc.Tests.Fakes;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcCapturePoller
{
    public class WhenRunning
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly PollerHarness<Row> _harness = new();
        private readonly Processor _processor = new();

        public WhenRunning() => _harness.Processor = _processor;

        [Fact]
        public async Task MustRelayChangesAsTheyAreCommitted()
        {
            Insert(1);

            await RunUntilAsync(async () =>
            {
                await WaitForAsync(() => _processor.Ids.Count == 1);
                Insert(2);
                Insert(3);
                await WaitForAsync(() => _processor.Ids.Count == 3);
            });

            Assert.Equal(new[] { 1, 2, 3 }, _processor.Ids);
            Assert.Equal(_harness.Queries.Transactions[^1].Lsn, _harness.Checkpoint);
        }

        [Fact]
        public async Task MustStandByWhileAnotherInstanceHoldsTheLease()
        {
            _harness.Leases.CanAcquire = _ => false;
            Insert(1);

            await RunUntilAsync(() => WaitForAsync(() => _harness.Leases.Leases.Count >= 3));

            Assert.Equal(CdcPollerState.Standby, _harness.Poller.State);
            Assert.Empty(_processor.Ids);
            Assert.All(_harness.Leases.Leases, l => Assert.True(l.Disposed));
            Assert.All(_harness.Leases.Resources, r => Assert.Equal("Chatter.IntegrationEventRelay.Cdc:FAKEDB-CDC:DBO_EVENT1", r));
        }

        [Fact]
        public async Task MustTakeOverOnceTheLeaseIsFree()
        {
            _harness.Leases.CanAcquire = attempt => attempt >= 2;
            Insert(1);

            await RunUntilAsync(() => WaitForAsync(() => _processor.Ids.Count == 1));

            Assert.Equal(3, _harness.Leases.Leases.Count);
        }

        [Fact]
        public async Task MustReportFailingToTakeTheLeaseAsAFailureNotAStandby()
        {
            _harness.Leases.AcquireFails = _ => true;

            await RunUntilAsync(() => WaitForAsync(() => _harness.Poller.ConsecutiveFailures >= 3));

            Assert.Equal(CdcPollerState.Retrying, _harness.Poller.State);
            Assert.Contains(_harness.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Couldn't take the lease"));
        }

        [Fact]
        public async Task MustRecoverOnceTheLeaseCanBeTakenAgain()
        {
            _harness.Leases.AcquireFails = attempt => attempt < 2;
            Insert(1);

            await RunUntilAsync(() => WaitForAsync(() => _processor.Ids.Count == 1 && _harness.Poller.State == CdcPollerState.Active));

            Assert.Equal(0, _harness.Poller.ConsecutiveFailures);
        }

        [Fact]
        public async Task MustNotCheckpointABatchIfTheLeaseWasLostWhileProcessingIt()
        {
            // Each lease passes the check before the batch and fails the one before the checkpoint
            _harness.Leases.HeldChecks = 1;
            Insert(1);

            await RunUntilAsync(() => WaitForAsync(() => _harness.Leases.Leases.Count >= 3));

            Assert.Null(_harness.Checkpoint);
            Assert.True(_processor.Attempts[1] >= 2);
        }

        [Fact]
        public async Task MustStopPollingWhenTheLeaseIsLostAndTryToRetakeIt()
        {
            _harness.Leases.HeldChecks = 1;

            await RunUntilAsync(() => WaitForAsync(() => _harness.Leases.Leases.Count >= 3));

            Assert.Contains(_harness.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Lost the lease"));
            Assert.All(_harness.Leases.Leases.SkipLast(1), l => Assert.True(l.Disposed));
        }

        [Fact]
        public async Task MustRetryAFailedBatchWithoutPublishingAnythingAfterIt()
        {
            Insert(1);
            Insert(2);
            _harness.Options.MaxTransactionsPerBatch = 1;
            _processor.FailuresLeft = 3;

            await RunUntilAsync(() => WaitForAsync(() => _processor.Ids.Count == 2));

            Assert.Equal(new[] { 1, 2 }, _processor.Ids);
            Assert.Equal(4, _processor.Attempts[1]);
            Assert.Equal(3, _harness.Logger.Entries.Count(e => e.Level == LogLevel.Error));
            Assert.Contains(_harness.Logger.Entries, e => e.Message.Contains("recovered after 3"));
        }

        [Fact]
        public async Task MustLogCriticalOnceFailuresPersist()
        {
            _harness.Options.FailuresBeforeCritical = 2;
            Insert(1);
            _processor.FailuresLeft = int.MaxValue;

            await RunUntilAsync(() => WaitForAsync(() => _harness.Logger.Entries.Any(e => e.Level == LogLevel.Critical)));

            Assert.Equal(CdcPollerState.Retrying, _harness.Poller.State);
            Assert.True(_harness.Poller.ConsecutiveFailures >= 2);
            Assert.Null(_harness.Checkpoint);
        }

        [Fact]
        public async Task MustHaltOnAGapAndResumeOnceTheCheckpointIsReset()
        {
            var published = Insert(1);
            Insert(2);
            Insert(3);
            _harness.Checkpoint = published.Lsn;
            _harness.Queries.MinLsn = _harness.Queries.Transactions[2].Lsn;

            await RunUntilAsync(async () =>
            {
                await WaitForAsync(() => _harness.Poller.State == CdcPollerState.Gap);
                Assert.Contains(_harness.Logger.Entries, e => e.Level == LogLevel.Critical && e.Exception is CdcGapException);
                Assert.Empty(_processor.Ids);

                // An operator accepts the loss by moving the checkpoint
                _harness.Checkpoint = null;
                await WaitForAsync(() => _processor.Ids.Count == 1);
            });

            Assert.Equal(new[] { 3 }, _processor.Ids);
        }

        [Fact]
        public async Task MustRetryWhileTheCaptureInstanceIsMissing()
        {
            _harness.Queries.CaptureInstanceExists = false;
            Insert(1);

            await RunUntilAsync(async () =>
            {
                await WaitForAsync(() => _harness.Logger.Entries.Any(e => e.Exception is CdcCaptureInstanceNotFoundException));
                _harness.Queries.CaptureInstanceExists = true;
                await WaitForAsync(() => _processor.Ids.Count == 1);
            });
        }

        [Fact]
        public async Task MustStopWhenCancelled()
        {
            using var cts = new CancellationTokenSource();
            var run = _harness.Poller.RunAsync(cts.Token);
            await WaitForAsync(() => _harness.Poller.State == CdcPollerState.Active);

            cts.Cancel();

            await run.WaitAsync(Timeout);
            Assert.True(_harness.Leases.Leases.Single().Disposed);
        }

        [Fact]
        public void MustDoubleTheRetryDelayUpToTheMaximum()
        {
            var harness = new PollerHarness<Row>(new CdcSourceOptions { RetryDelay = TimeSpan.FromSeconds(1), MaxRetryDelay = TimeSpan.FromSeconds(10) });

            Assert.Equal(new[] { 1d, 2, 4, 8, 10, 10 }, new[] { 1, 2, 3, 4, 5, 100 }.Select(f => harness.Poller.GetRetryDelay(f).TotalSeconds));
        }

        private async Task RunUntilAsync(Func<Task> condition)
        {
            using var cts = new CancellationTokenSource();
            var run = _harness.Poller.RunAsync(cts.Token);
            try
            {
                await condition();
            }
            finally
            {
                cts.Cancel();
                await run.WaitAsync(Timeout);
            }
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("The condition wasn't met in time.");
                await Task.Delay(5);
            }
        }

        private FakeTransaction Insert(int id) => _harness.Queries.Commit((CdcChangeRow.Insert, PollerHarness<Row>.Columns(id, $"row {id}")));

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class Processor : IRowChangeProcessor<Row>
        {
            private readonly ConcurrentQueue<int> _ids = new();

            public int FailuresLeft { get; set; }
            public ConcurrentDictionary<int, int> Attempts { get; } = new();
            public IReadOnlyList<int> Ids => _ids.ToList();

            public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
            {
                Attempts.AddOrUpdate(change.NewValue.Id, 1, (_, n) => n + 1);
                if (FailuresLeft > 0)
                {
                    FailuresLeft--;
                    throw new InvalidOperationException("publish failed");
                }

                _ids.Enqueue(change.NewValue.Id);
                return Task.CompletedTask;
            }
        }
    }
}
