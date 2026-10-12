using Chatter.CQRS.Context;
using Chatter.IntegrationEventRelay.Core;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.ServiceBroker.Handlers;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Tests.Handlers
{
    public class WhenHandlingRowChangedEvents
    {
        private readonly Mock<IRowChangeProcessor<FakeSourceEvent>> _processor = new();
        private readonly ServiceBrokerBinding<FakeSourceEvent> _binding = new("fakedb-ssb", new SourceTable("Event1", Database: "FakeDb"));
        private readonly Mock<IMessageHandlerContext> _context = new();
        private readonly CancellationTokenSource _cts = new();
        private RowChange<FakeSourceEvent> _change;
        private CancellationToken _token;

        public WhenHandlingRowChangedEvents()
        {
            _context.SetupGet(c => c.CancellationToken).Returns(_cts.Token);
            _processor.Setup(p => p.ProcessAsync(It.IsAny<RowChange<FakeSourceEvent>>(), It.IsAny<CancellationToken>()))
                .Callback<RowChange<FakeSourceEvent>, CancellationToken>((change, token) => { _change = change; _token = token; })
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task MustProcessAnInsertedRow()
        {
            var inserted = new FakeSourceEvent();
            var sut = new RowInsertedEventHandler<FakeSourceEvent>(_binding, _processor.Object, NullLogger<RowInsertedEventHandler<FakeSourceEvent>>.Instance);

            await sut.Handle(new RowInsertedEvent<FakeSourceEvent>(inserted), _context.Object);

            Assert.Equal(RowOperation.Insert, _change.Operation);
            Assert.Null(_change.OldValue);
            Assert.Same(inserted, _change.NewValue);
        }

        [Fact]
        public async Task MustProcessAnUpdatedRowWithItsOldAndNewValues()
        {
            var oldValue = new FakeSourceEvent();
            var newValue = new FakeSourceEvent();
            var sut = new RowUpdatedEventHandler<FakeSourceEvent>(_binding, _processor.Object, NullLogger<RowUpdatedEventHandler<FakeSourceEvent>>.Instance);

            await sut.Handle(new RowUpdatedEvent<FakeSourceEvent>(newValue, oldValue), _context.Object);

            Assert.Equal(RowOperation.Update, _change.Operation);
            Assert.Same(oldValue, _change.OldValue);
            Assert.Same(newValue, _change.NewValue);
        }

        [Fact]
        public async Task MustProcessADeletedRowAsTheNewValue()
        {
            var deleted = new FakeSourceEvent();
            var sut = new RowDeletedEventHandler<FakeSourceEvent>(_binding, _processor.Object, NullLogger<RowDeletedEventHandler<FakeSourceEvent>>.Instance);

            await sut.Handle(new RowDeletedEvent<FakeSourceEvent>(deleted), _context.Object);

            Assert.Equal(RowOperation.Delete, _change.Operation);
            Assert.Null(_change.OldValue);
            Assert.Same(deleted, _change.NewValue);
        }

        [Fact]
        public async Task MustDescribeTheChangeWithTheBindingAndNoPositionOrCommitTime()
        {
            var sut = new RowInsertedEventHandler<FakeSourceEvent>(_binding, _processor.Object, NullLogger<RowInsertedEventHandler<FakeSourceEvent>>.Instance);

            await sut.Handle(new RowInsertedEvent<FakeSourceEvent>(new FakeSourceEvent()), _context.Object);

            Assert.Equal("fakedb-ssb", _change.SourceName);
            Assert.Equal(_binding.Table, _change.Table);
            Assert.Null(_change.Position);
            Assert.Null(_change.CommittedAt);
        }

        [Fact]
        public async Task MustPassTheReceiveCancellationToken()
        {
            var sut = new RowInsertedEventHandler<FakeSourceEvent>(_binding, _processor.Object, NullLogger<RowInsertedEventHandler<FakeSourceEvent>>.Instance);

            await sut.Handle(new RowInsertedEvent<FakeSourceEvent>(new FakeSourceEvent()), _context.Object);

            Assert.Equal(_cts.Token, _token);
        }

        [Fact]
        public async Task MustPropagateProcessingFailuresSoTheMessageIsNotAcknowledged()
        {
            _processor.Setup(p => p.ProcessAsync(It.IsAny<RowChange<FakeSourceEvent>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("publish failed"));
            var sut = new RowUpdatedEventHandler<FakeSourceEvent>(_binding, _processor.Object, NullLogger<RowUpdatedEventHandler<FakeSourceEvent>>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.Handle(new RowUpdatedEvent<FakeSourceEvent>(new FakeSourceEvent(), new FakeSourceEvent()), _context.Object));
        }

        [Fact]
        public void MustThrowWithoutABinding()
            => Assert.Throws<ArgumentNullException>(() => new RowInsertedEventHandler<FakeSourceEvent>(null, _processor.Object, NullLogger<RowInsertedEventHandler<FakeSourceEvent>>.Instance));

        [Fact]
        public void MustThrowWithoutAProcessor()
            => Assert.Throws<ArgumentNullException>(() => new RowUpdatedEventHandler<FakeSourceEvent>(_binding, null, NullLogger<RowUpdatedEventHandler<FakeSourceEvent>>.Instance));

        [Fact]
        public void MustThrowWithoutALogger()
            => Assert.Throws<ArgumentNullException>(() => new RowDeletedEventHandler<FakeSourceEvent>(_binding, _processor.Object, null));

        public class FakeSourceEvent : ISourceEvent { }
    }
}
