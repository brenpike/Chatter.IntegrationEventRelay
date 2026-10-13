using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Contracts
{
    /// <summary>
    /// What every change source adapter must do: deliver captured changes as <see cref="RowChange{TSource}"/>s with
    /// the right values, acknowledge them only once processing succeeds, and honour the capabilities it declares.
    /// Each adapter's test project derives from it with an <see cref="IChangeSourceContractAdapter"/>.
    /// </summary>
    public abstract class ChangeSourceContractTests<TAdapter> where TAdapter : IChangeSourceContractAdapter, new()
    {
        protected TAdapter Adapter { get; } = new();

        private ChangeSourceCapabilities Capabilities => Adapter.Source.Capabilities;

        [Fact]
        public async Task MustDeliverAnInsertWithTheInsertedRow()
        {
            var row = Row(1, "inserted");
            Adapter.Capture(CapturedChange.Insert(row));

            var change = Assert.Single(await DeliverAsync());

            Assert.Equal(RowOperation.Insert, change.Operation);
            Assert.Null(change.OldValue);
            Assert.Equal(row, change.NewValue);
        }

        [Fact]
        public async Task MustDeliverAnUpdateWithTheOldAndNewRows()
        {
            var oldValue = Row(1, "before");
            var newValue = Row(1, "after");
            Adapter.Capture(CapturedChange.Update(oldValue, newValue));

            var change = Assert.Single(await DeliverAsync());

            Assert.Equal(RowOperation.Update, change.Operation);
            Assert.Equal(oldValue, change.OldValue);
            Assert.Equal(newValue, change.NewValue);
        }

        [Fact]
        public async Task MustDeliverADeleteWithTheDeletedRowAsTheNewValue()
        {
            var row = Row(1, "deleted");
            Adapter.Capture(CapturedChange.Delete(row));

            var change = Assert.Single(await DeliverAsync());

            Assert.Equal(RowOperation.Delete, change.Operation);
            Assert.Null(change.OldValue);
            Assert.Equal(row, change.NewValue);
        }

        [Fact]
        public async Task MustNameItselfAsTheChangesSource()
        {
            Adapter.Capture(CapturedChange.Insert(Row(1)));

            Assert.Equal(Adapter.Source.Name, Assert.Single(await DeliverAsync()).SourceName);
        }

        [Fact]
        public async Task MustAcknowledgeChangesOnceTheyAreProcessed()
        {
            Adapter.Capture(CapturedChange.Insert(Row(1)));
            var processor = new RecordingProcessor();

            Assert.True(await Adapter.DeliverAsync(processor, CancellationToken.None));
            await Adapter.DeliverAsync(processor, CancellationToken.None);

            Assert.Single(processor.Changes);
        }

        [Fact]
        public async Task MustNotAcknowledgeAChangeWhoseProcessingFails()
        {
            var row = Row(1);
            Adapter.Capture(CapturedChange.Insert(row));

            Assert.False(await Adapter.DeliverAsync(new RecordingProcessor(fail: true), CancellationToken.None));
            var redelivered = await DeliverAsync();

            Assert.Equal(row, Assert.Single(redelivered).NewValue);
        }

        [Fact]
        public async Task MustDeliverChangesInCommitOrderIfOrdered()
        {
            if (!Capabilities.HasFlag(ChangeSourceCapabilities.Ordered))
                return;

            var rows = Enumerable.Range(1, 5).Select(i => Row(i)).ToList();
            Adapter.Capture(CapturedChange.Insert(rows[0]));
            Adapter.Capture(CapturedChange.Update(rows[0], rows[1]));
            Adapter.Capture(CapturedChange.Insert(rows[2]));
            Adapter.Capture(CapturedChange.Delete(rows[2]));
            Adapter.Capture(CapturedChange.Insert(rows[3]));

            var changes = await DeliverAsync();

            Assert.Equal(new[] { RowOperation.Insert, RowOperation.Update, RowOperation.Insert, RowOperation.Delete, RowOperation.Insert }, changes.Select(c => c.Operation));
            Assert.Equal(new[] { rows[0], rows[1], rows[2], rows[2], rows[3] }, changes.Select(c => c.NewValue));
        }

        [Fact]
        public async Task MustGiveEachChangeAPositionThatSurvivesRedeliveryIfResumable()
        {
            Adapter.Capture(CapturedChange.Insert(Row(1)));
            Adapter.Capture(CapturedChange.Update(Row(1), Row(1, "changed")));

            var failed = new RecordingProcessor(fail: true, failAfter: 1);
            await Adapter.DeliverAsync(failed, CancellationToken.None);
            var redelivered = await DeliverAsync();

            if (!Capabilities.HasFlag(ChangeSourceCapabilities.Resumable))
            {
                Assert.All(redelivered, c => Assert.Null(c.Position));
                return;
            }

            Assert.Equal(2, redelivered.Count);
            Assert.All(redelivered, c => Assert.NotNull(c.Position));
            Assert.NotEqual(redelivered[0].Position, redelivered[1].Position);
            Assert.Equal(failed.Changes.First().Position, redelivered[0].Position);
        }

        [Fact]
        public async Task MustReportCommitTimesOnlyIfItHasTheCapability()
        {
            Adapter.Capture(CapturedChange.Insert(Row(1)));

            var change = Assert.Single(await DeliverAsync());

            if (Capabilities.HasFlag(ChangeSourceCapabilities.CommitTime))
                Assert.NotNull(change.CommittedAt);
            else
                Assert.Null(change.CommittedAt);
        }

        protected static ContractRow Row(int id, string name = "row") => new() { Id = id, Name = $"{name} {id}" };

        private async Task<IReadOnlyList<RowChange<ContractRow>>> DeliverAsync()
        {
            var processor = new RecordingProcessor();
            Assert.True(await Adapter.DeliverAsync(processor, CancellationToken.None), "The adapter didn't acknowledge changes that were processed.");
            return processor.Changes;
        }

        /// <summary>
        /// Records the changes it's given; optionally fails after processing <c>failAfter</c> of them.
        /// </summary>
        protected sealed class RecordingProcessor : IRowChangeProcessor<ContractRow>
        {
            private readonly bool _fail;
            private readonly int _failAfter;
            private readonly List<RowChange<ContractRow>> _changes = new();

            public RecordingProcessor(bool fail = false, int failAfter = 0)
            {
                _fail = fail;
                _failAfter = failAfter;
            }

            public IReadOnlyList<RowChange<ContractRow>> Changes => _changes;

            public Task ProcessAsync(RowChange<ContractRow> change, CancellationToken cancellationToken)
            {
                if (_fail && _changes.Count >= _failAfter)
                    throw new InvalidOperationException("Processing failed");

                _changes.Add(change);
                return Task.CompletedTask;
            }
        }
    }
}
