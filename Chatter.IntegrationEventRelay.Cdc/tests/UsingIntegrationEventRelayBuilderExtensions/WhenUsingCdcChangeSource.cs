using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Cdc.Extensions;
using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Builder;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingIntegrationEventRelayBuilderExtensions
{
    public class WhenUsingCdcChangeSource
    {
        private const string ConnectionString = "Data Source=db;Database=FakeDb;Integrated Security=True;";
        private readonly ServiceCollection _services = new();
        private readonly EventMappingConfiguration _configuration = new() { ConnectionString = ConnectionString };

        public WhenUsingCdcChangeSource()
        {
            _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            _services.AddSingleton(_configuration);
        }

        [Fact]
        public void MustRegisterEveryConfiguredCdcSourceOnce()
        {
            Source("fakedb-cdc");
            Source("unused-cdc");
            _configuration.Sources["fakedb-ssb"] = new ChangeSourceConfiguration { Type = "ServiceBroker" };
            Map(Mapping("fakedb-cdc"), Mapping("fakedb-cdc", RowOperation.Delete));

            Use();

            var sources = Provider().GetServices<IChangeSource>().ToList();
            Assert.Equal(new[] { "fakedb-cdc", "unused-cdc" }, sources.Select(s => s.Name).OrderBy(n => n));
            Assert.All(sources, s => Assert.IsType<CdcChangeSource>(s));
        }

        [Fact]
        public void MustIgnoreMappingsForOtherSourceTypes()
        {
            Map(Mapping(source: null));

            Use();

            Assert.Empty(Provider().GetServices<IChangeSource>());
            Assert.Empty(Pollers());
        }

        [Fact]
        public void MustPollEachCaptureInstanceOnce()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"), Mapping("fakedb-cdc", RowOperation.Delete), Mapping("fakedb-cdc", table: "Event2", sourceEventType: typeof(AnotherRow)));

            Use();

            Assert.Equal(new[] { "dbo_Event1", "dbo_Event2" }, Pollers().Select(p => p.Capture.CaptureInstance));
            Assert.IsType<CdcCapturePoller<Row>>(Pollers()[0]);
            Assert.IsType<CdcCapturePoller<AnotherRow>>(Pollers()[1]);
        }

        [Fact]
        public void MustRunThePollersInOneHostedService()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"));

            Use();
            new FakeRelayBuilder(_services, _configuration).UseCdcChangeSource();

            Assert.Single(Provider().GetServices<IHostedService>().OfType<CdcPollerService>());
        }

        [Fact]
        public void MustDescribeTheCapture()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc", database: null));

            Use();

            var capture = Pollers().Single().Capture;
            Assert.Equal("fakedb-cdc", capture.SourceName);
            Assert.Equal(new SourceTable("Event1", "dbo", "FakeDb"), capture.Table);
            Assert.Equal(typeof(Row), capture.SourceEventType);
        }

        [Fact]
        public void MustReadTheCaptureInstanceAndTableSchemaFromTheMapping()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc", settings: new() { ["CaptureInstance"] = "sales_Event1_v2", ["TableSchema"] = "sales" }));

            Use();

            var capture = Pollers().Single().Capture;
            Assert.Equal("sales_Event1_v2", capture.CaptureInstance);
            Assert.Equal("sales", capture.Table.Schema);
        }

        [Fact]
        public void MustNameTheCaptureInstanceAfterTheTableByDefault()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc", settings: new() { ["TableSchema"] = "sales" }));

            Use();

            Assert.Equal("sales_Event1", Pollers().Single().Capture.CaptureInstance);
        }

        [Fact]
        public void MustBindTheSourcesOptionsAndThenApplyTheDelegate()
        {
            Source("fakedb-cdc", new() { ["PollInterval"] = "00:00:05", ["MaxTransactionsPerBatch"] = "50", ["StartPosition"] = "Latest" });
            Map(Mapping("fakedb-cdc"));

            Use(o => o.MaxTransactionsPerBatch = 25);

            var options = Pollers().Single().Capture.Options;
            Assert.Equal(TimeSpan.FromSeconds(5), options.PollInterval);
            Assert.Equal(25, options.MaxTransactionsPerBatch);
            Assert.Equal(CdcStartPosition.Latest, options.StartPosition);
        }

        [Fact]
        public void MustRegisterSourcesThatParkRowsAsUnordered()
        {
            Source("ordered-cdc");
            Source("parking-cdc", new() { ["ErrorPolicy"] = "ParkRow" });
            Map(Mapping("ordered-cdc"), Mapping("parking-cdc", table: "Event2", sourceEventType: typeof(AnotherRow)));

            Use();

            var sources = Provider().GetServices<IChangeSource>().ToDictionary(s => s.Name);
            Assert.True(sources["ordered-cdc"].Capabilities.HasFlag(ChangeSourceCapabilities.Ordered));
            Assert.False(sources["parking-cdc"].Capabilities.HasFlag(ChangeSourceCapabilities.Ordered));
        }

        [Fact]
        public void MustRejectInvalidOptions()
        {
            Source("fakedb-cdc", new() { ["PollInterval"] = "00:00:00" });
            Map(Mapping("fakedb-cdc"));

            var e = Assert.Throws<ArgumentException>(() => Use());
            Assert.Contains("PollInterval", e.Message);
        }

        [Fact]
        public void MustRejectACaptureInstanceMappedWithMoreThanOneSourceEventType()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"), Mapping("fakedb-cdc", RowOperation.Delete, sourceEventType: typeof(AnotherRow)));

            var e = Assert.Throws<ArgumentException>(() => Use());
            Assert.Contains("more than one", e.Message);
        }

        [Fact]
        public void MustRejectCaptureInstancesWhoseChangesCantBeToldApart()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"), Mapping("fakedb-cdc", RowOperation.Delete, settings: new() { ["TableSchema"] = "archive" }));

            var e = Assert.Throws<ArgumentException>(() => Use());
            Assert.Contains("'dbo_Event1', 'archive_Event1'", e.Message);
        }

        [Fact]
        public void MustAllowTablesWithTheSameNameIntoDifferentSourceEventTypes()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"), Mapping("fakedb-cdc", settings: new() { ["TableSchema"] = "archive" }, sourceEventType: typeof(AnotherRow)));

            Use();

            Assert.Equal(2, Pollers().Count);
        }

        [Fact]
        public void MustRejectASourceEventTypeWithoutAParameterlessConstructor()
        {
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc", sourceEventType: typeof(RowWithoutParameterlessConstructor)));

            var e = Assert.Throws<ArgumentException>(() => Use());
            Assert.Contains("parameterless constructor", e.Message);
        }

        [Fact]
        public void MustRejectAnEmptyRelaySchema()
        {
            _configuration.Schema = " ";
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"));

            Assert.Throws<ArgumentException>(() => Use());
        }

        [Fact]
        public void MustRequireAConnectionString()
        {
            _configuration.ConnectionString = null;
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"));

            Assert.Throws<ArgumentNullException>(() => Use());
        }

        [Fact]
        public void MustRecordEachCaptureInstanceForMigrations()
        {
            _configuration.Schema = "relay";
            Source("fakedb-cdc");
            Map(Mapping("fakedb-cdc"), Mapping("fakedb-cdc", RowOperation.Delete));

            Use();

            var target = Assert.Single(Provider().GetServices<CdcMigrationTarget>().Distinct());
            Assert.Equal(new CdcMigrationTarget(ConnectionString, "relay", "dbo", "Event1", "dbo_Event1"), target);
        }

        [Fact]
        public void MustReturnTheSameBuilder()
        {
            var builder = new FakeRelayBuilder(_services, _configuration);
            Assert.Same(builder, builder.UseCdcChangeSource());
        }

        private void Use(Action<CdcSourceOptions> configure = null) => new FakeRelayBuilder(_services, _configuration).UseCdcChangeSource(configure);

        private ServiceProvider Provider()
        {
            _services.AddScoped(typeof(IRowChangeProcessor<>), typeof(NoProcessor<>));
            return _services.BuildServiceProvider();
        }

        private IReadOnlyList<ICdcCapturePoller> Pollers() => Provider().GetServices<ICdcCapturePoller>().ToList();

        private void Source(string name, Dictionary<string, string> settings = null)
            => _configuration.Sources[name] = new ChangeSourceConfiguration { Type = "Cdc", Section = Section(settings) };

        private void Map(params EventMappingConfigurationItem[] mappings) => _configuration.Mappings = mappings;

        private static EventMappingConfigurationItem Mapping(string source, RowOperation operation = RowOperation.Update, string table = "Event1", string database = "FakeDb",
            Type sourceEventType = null, Dictionary<string, string> settings = null)
            => new()
            {
                Source = source,
                SourceChangeType = operation,
                TableName = table,
                DatabaseName = database,
                SourceEventType = sourceEventType ?? typeof(Row),
                SourceEventTypeName = (sourceEventType ?? typeof(Row)).FullName,
                IntegrationEventType = typeof(FakeEvent),
                IntegrationEventTypeName = typeof(FakeEvent).FullName,
                Section = Section(settings)
            };

        private static IConfigurationSection Section(Dictionary<string, string> settings)
            => new ConfigurationBuilder().AddInMemoryCollection((settings ?? new()).ToDictionary(s => $"Section:{s.Key}", s => s.Value)).Build().GetSection("Section");

        public class Row { }
        public class AnotherRow { }
        public class RowWithoutParameterlessConstructor
        {
            public RowWithoutParameterlessConstructor(int id) { }
        }
        public class FakeEvent : IEvent { }

        private sealed class NoProcessor<T> : IRowChangeProcessor<T> where T : class
        {
            public System.Threading.Tasks.Task ProcessAsync(RowChange<T> change, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private sealed class FakeRelayBuilder : IIntegrationEventRelayBuilder
        {
            public FakeRelayBuilder(IServiceCollection services, EventMappingConfiguration configuration)
            {
                Services = services;
                Configuration = configuration;
            }

            public IChatterBuilder ChatterBuilder => throw new NotSupportedException();
            public IServiceCollection Services { get; }
            public EventMappingConfiguration Configuration { get; }
        }
    }
}
