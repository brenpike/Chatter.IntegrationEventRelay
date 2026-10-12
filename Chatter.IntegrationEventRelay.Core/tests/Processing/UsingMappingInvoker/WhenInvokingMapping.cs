using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.Core.Publishing;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Processing.UsingMappingInvoker
{
    public class WhenInvokingMapping : MockContext
    {
        private readonly Mock<IMapSourceInsertToIntegrationEvent<FakeSource, FakeEvent>> _insertMapper = new();
        private readonly Mock<IMapSourceUpdateToIntegrationEvent<FakeSource, FakeEvent>> _updateMapper = new();
        private readonly Mock<IMapSourceDeleteToIntegrationEvent<FakeSource, FakeEvent>> _deleteMapper = new();
        private readonly Mock<IIntegrationEventPublisher> _publisher = new();
        private readonly EventMappingConfigurationItem _mapping;
        private readonly MappingInvoker<FakeSource, FakeEvent> _sut;
        private readonly FakeEvent _event = new();

        public WhenInvokingMapping()
        {
            _insertMapper.Setup(MapAnything<IMapSourceInsertToIntegrationEvent<FakeSource, FakeEvent>>()).ReturnsAsync(_event);
            _updateMapper.Setup(MapAnything<IMapSourceUpdateToIntegrationEvent<FakeSource, FakeEvent>>()).ReturnsAsync(_event);
            _deleteMapper.Setup(MapAnything<IMapSourceDeleteToIntegrationEvent<FakeSource, FakeEvent>>()).ReturnsAsync(_event);

            var services = new ServiceCollection()
                .AddSingleton(_insertMapper.Object)
                .AddSingleton(_updateMapper.Object)
                .AddSingleton(_deleteMapper.Object)
                .BuildServiceProvider();

            _mapping = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;
            _sut = new MappingInvoker<FakeSource, FakeEvent>(services, _publisher.Object, NullLogger<MappingInvoker<FakeSource, FakeEvent>>.Instance);
        }

        [Theory]
        [InlineData(RowOperation.Insert)]
        [InlineData(RowOperation.Update)]
        [InlineData(RowOperation.Delete)]
        public async Task MustUseTheMapperForTheOperation(RowOperation operation)
        {
            await _sut.InvokeAsync(Change(operation), _mapping, CancellationToken.None);

            _insertMapper.Verify(MapAnything<IMapSourceInsertToIntegrationEvent<FakeSource, FakeEvent>>(), operation == RowOperation.Insert ? Times.Once() : Times.Never());
            _updateMapper.Verify(MapAnything<IMapSourceUpdateToIntegrationEvent<FakeSource, FakeEvent>>(), operation == RowOperation.Update ? Times.Once() : Times.Never());
            _deleteMapper.Verify(MapAnything<IMapSourceDeleteToIntegrationEvent<FakeSource, FakeEvent>>(), operation == RowOperation.Delete ? Times.Once() : Times.Never());
        }

        [Fact]
        public async Task MustPassOldAndNewValuesAndTheMappingToTheMapper()
        {
            var change = Change(RowOperation.Update);

            await _sut.InvokeAsync(change, _mapping, CancellationToken.None);

            _updateMapper.Verify(m => m.MapAsync(
                It.Is<MappingData<FakeSource>>(d => d.OldValue == change.OldValue && d.NewValue == change.NewValue),
                It.IsAny<RelayContext>(),
                _mapping), Times.Once());
        }

        [Fact]
        public async Task MustDescribeTheChangeInTheRelayContext()
        {
            var change = Change(RowOperation.Update, new ChangePosition("lsn"));
            using var cts = new CancellationTokenSource();

            await _sut.InvokeAsync(change, _mapping, cts.Token);

            _updateMapper.Verify(m => m.MapAsync(
                It.IsAny<MappingData<FakeSource>>(),
                It.Is<RelayContext>(c => c.SourceName == change.SourceName
                    && c.Operation == change.Operation
                    && c.Table == change.Table
                    && c.Position == change.Position
                    && c.CommittedAt == change.CommittedAt
                    && c.CancellationToken == cts.Token),
                It.IsAny<EventMappingConfigurationItem>()), Times.Once());
        }

        [Fact]
        public async Task MustPublishTheMappedEventWithTheMapping()
        {
            await _sut.InvokeAsync(Change(RowOperation.Insert), _mapping, CancellationToken.None);

            _publisher.Verify(p => p.PublishAsync(_event, _mapping, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task MustNotPublishWhenTheMapperReturnsNull()
        {
            _insertMapper.Setup(MapAnything<IMapSourceInsertToIntegrationEvent<FakeSource, FakeEvent>>()).ReturnsAsync((FakeEvent)null);

            await _sut.InvokeAsync(Change(RowOperation.Insert), _mapping, CancellationToken.None);

            _publisher.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MustLeaveTheMessageIdToTheBrokerWhenTheChangeHasNoPosition()
        {
            await _sut.InvokeAsync(Change(RowOperation.Insert), _mapping, CancellationToken.None);

            _publisher.Verify(p => p.PublishAsync(It.IsAny<FakeEvent>(), It.IsAny<EventMappingConfigurationItem>(), null, It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task MustPublishWithTheMessageIdDerivedFromThePosition()
        {
            var change = Change(RowOperation.Insert, new ChangePosition("0x0001:0x0002"));

            await _sut.InvokeAsync(change, _mapping, CancellationToken.None);

            _publisher.Verify(p => p.PublishAsync(It.IsAny<FakeEvent>(), It.IsAny<EventMappingConfigurationItem>(), MappingInvoker<FakeSource, FakeEvent>.GetMessageId(change), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public void MustNotConfuseWhereTheSourceNameEndsAndThePositionStarts()
        {
            var first = MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, new ChangePosition("c")) with { SourceName = "a:b" });
            var second = MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, new ChangePosition("b:c")) with { SourceName = "a" });

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void MustGiveDifferentPositionsDifferentMessageIds()
        {
            Assert.NotEqual(
                MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, new ChangePosition("0x0001"))),
                MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, new ChangePosition("0x0002"))));
        }

        [Fact]
        public void MustGiveTheSameMessageIdWhenAChangeIsReplayed()
        {
            var change = Change(RowOperation.Insert, new ChangePosition("0x0001"));
            Assert.Equal(MappingInvoker<FakeSource, FakeEvent>.GetMessageId(change), MappingInvoker<FakeSource, FakeEvent>.GetMessageId(change with { NewValue = new FakeSource() }));
        }

        [Fact]
        public void MustGiveEventTypesWithTheSameNameDifferentMessageIds()
        {
            var change = Change(RowOperation.Insert, new ChangePosition("0x0001"));
            Assert.NotEqual(MappingInvoker<FakeSource, FakeEvent>.GetMessageId(change), MappingInvoker<FakeSource, OtherNamespace.FakeEvent>.GetMessageId(change));
        }

        [Fact]
        public void MustKeepMessageIdsShortWhateverTheLengthOfTheSourceNameAndPosition()
        {
            var id = MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, new ChangePosition(new string('p', 500))) with { SourceName = new string('s', 500) });
            Assert.Equal(32, id.Length);
        }

        [Fact]
        public async Task MustGiveTheSamePositionFromDifferentSourcesDifferentMessageIds()
        {
            var position = new ChangePosition("0x0001");
            var first = MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, position) with { SourceName = "db1-cdc" });
            var second = MappingInvoker<FakeSource, FakeEvent>.GetMessageId(Change(RowOperation.Insert, position) with { SourceName = "db2-cdc" });

            Assert.NotEqual(first, second);
        }

        [Fact]
        public async Task MustPropagateAMapperFailureWithoutPublishing()
        {
            _insertMapper.Setup(MapAnything<IMapSourceInsertToIntegrationEvent<FakeSource, FakeEvent>>()).ThrowsAsync(new InvalidOperationException("mapping failed"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.InvokeAsync(Change(RowOperation.Insert), _mapping, CancellationToken.None));

            _publisher.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MustPropagateAPublishFailure()
        {
            _publisher.Setup(p => p.PublishAsync(It.IsAny<FakeEvent>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.InvokeAsync(Change(RowOperation.Insert), _mapping, CancellationToken.None));
        }

        [Fact]
        public void MustReportItsIntegrationEventType()
            => Assert.Equal(typeof(FakeEvent), _sut.IntegrationEventType);

        private static System.Linq.Expressions.Expression<Func<TMapper, Task<FakeEvent>>> MapAnything<TMapper>()
            where TMapper : IMapSourceToIntegrationEvent<FakeSource, FakeEvent>
            => m => m.MapAsync(It.IsAny<MappingData<FakeSource>>(), It.IsAny<RelayContext>(), It.IsAny<EventMappingConfigurationItem>());

        private static RowChange<FakeSource> Change(RowOperation operation, ChangePosition position = null)
            => new("source", operation, operation == RowOperation.Update ? new FakeSource() : null, new FakeSource(), new SourceTable("Table"), position, DateTimeOffset.UnixEpoch);

        public class FakeSource { }
        public class FakeEvent : IEvent { }
    }

    namespace OtherNamespace
    {
        public class FakeEvent : IEvent { }
    }
}
