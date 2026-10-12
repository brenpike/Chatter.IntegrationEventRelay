using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Processing.UsingRowChangeProcessor
{
    public class WhenProcessingRowChange : MockContext
    {
        private readonly Mock<IMappingInvoker<FakeSource>> _fakeEventInvoker = new();
        private readonly Mock<IMappingInvoker<FakeSource>> _otherEventInvoker = new();
        private readonly List<string> _invocations = new();

        public WhenProcessingRowChange()
        {
            _fakeEventInvoker.SetupGet(i => i.IntegrationEventType).Returns(typeof(FakeEvent));
            _fakeEventInvoker.Setup(i => i.InvokeAsync(It.IsAny<RowChange<FakeSource>>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<CancellationToken>()))
                .Callback(() => _invocations.Add(nameof(FakeEvent)))
                .Returns(Task.CompletedTask);
            _otherEventInvoker.SetupGet(i => i.IntegrationEventType).Returns(typeof(OtherFakeEvent));
            _otherEventInvoker.Setup(i => i.InvokeAsync(It.IsAny<RowChange<FakeSource>>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<CancellationToken>()))
                .Callback(() => _invocations.Add(nameof(OtherFakeEvent)))
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task MustInvokeEveryMatchingMappingInConfigurationOrder()
        {
            var sut = CreateSut(Mapping<OtherFakeEvent>(RowOperation.Update), Mapping<FakeEvent>(RowOperation.Update));

            await sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None);

            Assert.Equal(new[] { nameof(OtherFakeEvent), nameof(FakeEvent) }, _invocations);
        }

        [Fact]
        public async Task MustPassTheChangeAndItsMappingToTheInvoker()
        {
            var mapping = Mapping<FakeEvent>(RowOperation.Insert);
            var change = Change(RowOperation.Insert);
            var sut = CreateSut(mapping);

            await sut.ProcessAsync(change, CancellationToken.None);

            _fakeEventInvoker.Verify(i => i.InvokeAsync(change, mapping, It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task MustSkipMappingsForOtherOperations()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Insert), Mapping<OtherFakeEvent>(RowOperation.Delete));

            await sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None);

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustSkipMappingsForOtherSourceEventTypes()
        {
            var mapping = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(OtherFakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(RowOperation.Update)
                .Mock;
            var sut = CreateSut(mapping);

            await sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None);

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustSkipMappingsForOtherSources()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, source: "cdc"));

            await sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None);

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustMatchMappingsWithoutASourceToTheDefaultSource()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, source: null));

            await sut.ProcessAsync(Change(RowOperation.Update, sourceName: EventMappingConfiguration.DefaultSourceName), CancellationToken.None);

            Assert.Single(_invocations);
        }

        [Fact]
        public async Task MustSkipMappingsForOtherTablesWhenTheSourceReportsATable()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, table: "OtherTable"));

            await sut.ProcessAsync(Change(RowOperation.Update, tableName: "Table"), CancellationToken.None);

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustMatchTableNamesIgnoringCase()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, table: "TABLE"));

            await sut.ProcessAsync(Change(RowOperation.Update, tableName: "table"), CancellationToken.None);

            Assert.Single(_invocations);
        }

        [Fact]
        public async Task MustSkipMappingsForOtherDatabasesWhenBothNameOne()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, database: "OtherDb"));

            await sut.ProcessAsync(Change(RowOperation.Update, database: "FakeDb"), CancellationToken.None);

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustMatchMappingsForTheReportedDatabase()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, database: "fakedb"), Mapping<OtherFakeEvent>(RowOperation.Update, database: "OtherDb"));

            await sut.ProcessAsync(Change(RowOperation.Update, database: "FakeDb"), CancellationToken.None);

            Assert.Equal(new[] { nameof(FakeEvent) }, _invocations);
        }

        [Fact]
        public async Task MustMatchMappingsWithoutADatabaseWhateverTheReportedDatabase()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, database: ""));

            await sut.ProcessAsync(Change(RowOperation.Update, database: "FakeDb"), CancellationToken.None);

            Assert.Single(_invocations);
        }

        [Fact]
        public async Task MustMatchAnyTableWhenTheSourceDoesNotReportOne()
        {
            var sut = CreateSut(Mapping<FakeEvent>(RowOperation.Update, table: "AnyTable"));

            await sut.ProcessAsync(Change(RowOperation.Update, tableName: null), CancellationToken.None);

            Assert.Single(_invocations);
        }

        [Fact]
        public async Task MustNotInvokeAnythingWhenThereAreNoMappings()
        {
            var sut = CreateSut();

            await sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None);

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustPropagateAnInvokerFailureAndStopProcessingTheChange()
        {
            _otherEventInvoker.Setup(i => i.InvokeAsync(It.IsAny<RowChange<FakeSource>>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mapper failed"));
            var sut = CreateSut(Mapping<OtherFakeEvent>(RowOperation.Update), Mapping<FakeEvent>(RowOperation.Update));

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None));

            Assert.Empty(_invocations);
        }

        [Fact]
        public async Task MustThrowWhenNoInvokerIsRegisteredForTheIntegrationEventType()
        {
            var sut = new RowChangeProcessor<FakeSource>(
                Context.Configuration().EventMappingConfiguration.WithMappings(Mapping<FakeEvent>(RowOperation.Update)).Mock,
                new[] { _otherEventInvoker.Object },
                NullLogger<RowChangeProcessor<FakeSource>>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProcessAsync(Change(RowOperation.Update), CancellationToken.None));
        }

        [Fact]
        public async Task MustProcessABatchInOrder()
        {
            var sut = (IRowChangeProcessor<FakeSource>)CreateSut(Mapping<FakeEvent>(RowOperation.Insert), Mapping<OtherFakeEvent>(RowOperation.Delete));

            await sut.ProcessBatchAsync(new[] { Change(RowOperation.Delete), Change(RowOperation.Insert) }, CancellationToken.None);

            Assert.Equal(new[] { nameof(OtherFakeEvent), nameof(FakeEvent) }, _invocations);
        }

        [Fact]
        public async Task MustStopABatchAtTheFirstFailure()
        {
            _otherEventInvoker.Setup(i => i.InvokeAsync(It.IsAny<RowChange<FakeSource>>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mapper failed"));
            var sut = (IRowChangeProcessor<FakeSource>)CreateSut(Mapping<FakeEvent>(RowOperation.Insert), Mapping<OtherFakeEvent>(RowOperation.Delete));

            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ProcessBatchAsync(new[] { Change(RowOperation.Delete), Change(RowOperation.Insert) }, CancellationToken.None));

            Assert.Empty(_invocations);
        }

        private RowChangeProcessor<FakeSource> CreateSut(params EventMappingConfigurationItem[] mappings)
            => new(Context.Configuration().EventMappingConfiguration.WithMappings(mappings).Mock,
                   new[] { _fakeEventInvoker.Object, _otherEventInvoker.Object },
                   NullLogger<RowChangeProcessor<FakeSource>>.Instance);

        private EventMappingConfigurationItem Mapping<TEvent>(RowOperation operation, string source = "source", string table = "Table", string database = "database")
            => Context.Configuration().EventMappingConfigurationItem
                .WithDatabaseName(database)
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(TEvent))
                .WithChangeType(operation)
                .WithSource(source)
                .WithTable(table)
                .Mock;

        private static RowChange<FakeSource> Change(RowOperation operation, string sourceName = "source", string tableName = "Table", string database = null)
            => new(sourceName, operation, null, new FakeSource(), tableName is null ? null : new SourceTable(tableName, Database: database), null, null);

        public class FakeSource { }
        public class OtherFakeSource { }
        public class FakeEvent : IEvent { }
        public class OtherFakeEvent : IEvent { }
    }
}
