using Chatter.CQRS;
using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.IntegrationEventRelay.ServiceBroker.Extensions;
using Chatter.IntegrationEventRelay.ServiceBroker.Handlers;
using Chatter.MessageBrokers.Receiving;
using Chatter.MessageBrokers.SqlServiceBroker.Configuration;
using Chatter.SqlChangeFeed;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Xunit;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Tests.Extensions.UsingIntegrationEventRelayBuilderExtensions
{
    public class WhenUsingServiceBrokerChangeSource : MockContext
    {
        private const string ConnectionString = "Data Source=sourceToIntegrationEvent;Database=FakeDb;Integrated Security=True;";
        private readonly IChatterBuilder _builder;

        public WhenUsingServiceBrokerChangeSource()
        {
            var assembly = Context.Common().Assembly.WithTypes(typeof(FakeSourceEvent), typeof(AnotherFakeSourceEvent)).Mock;
            var asf = Context.Cqrs().AssemblySourceFilter.WithAssemblySource(assembly).Mock;
            _builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(asf)
                .Mock;
        }

        [Fact]
        public void MustRegisterTheDefaultSourceForMappingsWithoutOne()
        {
            Use(Context.Configuration().EventMappingConfiguration.WithConnectionString(ConnectionString).WithMappings(Mapping()));

            var source = Assert.Single(ChangeSources());
            Assert.Equal(EventMappingConfiguration.DefaultSourceName, source.Name);
            Assert.IsType<ServiceBrokerChangeSource>(source);
        }

        [Fact]
        public void MustRegisterEveryConfiguredServiceBrokerSourceOnce()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithSource("fakedb-ssb", "ServiceBroker")
                .WithSource("unused-ssb", "ServiceBroker")
                .WithSource("fakedb-cdc", "Cdc")
                .WithMappings(Mapping(source: "fakedb-ssb"), Mapping(RowOperation.Delete, source: "fakedb-ssb"));

            Use(configuration);

            Assert.Equal(new[] { "fakedb-ssb", "unused-ssb" }, ChangeSources().Select(s => s.Name).OrderBy(n => n));
        }

        [Fact]
        public void MustIgnoreMappingsForOtherSourceTypes()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithSource("fakedb-cdc", "Cdc")
                .WithMappings(Mapping(source: "fakedb-cdc"));

            Use(configuration);

            Assert.Empty(ChangeSources());
            Assert.Empty(_builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowUpdatedEvent<FakeSourceEvent>>)));
            Assert.Empty(_builder.Services.GetServiceDescriptorsByServiceType(typeof(ServiceBrokerFeed)));
        }

        [Theory]
        [InlineData(RowOperation.Insert, typeof(IMessageHandler<RowInsertedEvent<FakeSourceEvent>>), typeof(RowInsertedEventHandler<FakeSourceEvent>))]
        [InlineData(RowOperation.Update, typeof(IMessageHandler<RowUpdatedEvent<FakeSourceEvent>>), typeof(RowUpdatedEventHandler<FakeSourceEvent>))]
        [InlineData(RowOperation.Delete, typeof(IMessageHandler<RowDeletedEvent<FakeSourceEvent>>), typeof(RowDeletedEventHandler<FakeSourceEvent>))]
        public void MustRegisterAHandlerForEachMappedOperation(RowOperation operation, Type serviceType, Type implementationType)
        {
            Use(Context.Configuration().EventMappingConfiguration.WithConnectionString(ConnectionString).WithMappings(Mapping(operation)));

            var sd = Assert.Single(_builder.Services.GetServiceDescriptorsByServiceType(serviceType));
            Assert.Equal(implementationType, sd.ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, sd.Lifetime);
        }

        [Fact]
        public void MustRegisterOneHandlerPerOperationWhateverTheNumberOfMappings()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(integrationEventType: typeof(FakeEvent)), Mapping(integrationEventType: typeof(AnotherFakeEvent)));

            Use(configuration);

            Assert.Single(_builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowUpdatedEvent<FakeSourceEvent>>)));
        }

        [Fact]
        public void MustNotRegisterHandlersForUnmappedOperations()
        {
            Use(Context.Configuration().EventMappingConfiguration.WithConnectionString(ConnectionString).WithMappings(Mapping(RowOperation.Update)));

            Assert.Empty(_builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowInsertedEvent<FakeSourceEvent>>)));
            Assert.Empty(_builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowDeletedEvent<FakeSourceEvent>>)));
        }

        [Fact]
        public void MustBindTheSourceEventTypeToItsSourceAndTable()
        {
            Use(Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithSource("fakedb-ssb", "ServiceBroker")
                .WithMappings(Mapping(source: "fakedb-ssb", table: "Event1", database: "FakeDb1")));

            var binding = _builder.Services.BuildServiceProvider().GetRequiredService<ServiceBrokerBinding<FakeSourceEvent>>();
            Assert.Equal("fakedb-ssb", binding.SourceName);
            Assert.Equal(new SourceTable("Event1", Database: "FakeDb1"), binding.Table);
        }

        [Fact]
        public void MustRecordEachFeedForMigrations()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(), Mapping(RowOperation.Delete), Mapping(sourceEventType: typeof(AnotherFakeSourceEvent), table: "Event2"));

            Use(configuration);

            var feeds = _builder.Services.BuildServiceProvider().GetServices<ServiceBrokerFeed>().Select(f => f.SourceEventType);
            Assert.Equal(new[] { typeof(FakeSourceEvent), typeof(AnotherFakeSourceEvent) }, feeds);
        }

        [Fact]
        public void MustAddASqlChangeFeedForEachSourceEventType()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(), Mapping(RowOperation.Delete), Mapping(sourceEventType: typeof(AnotherFakeSourceEvent), table: "Event2"));

            Use(configuration);

            Assert.Single(_builder.Services.GetServiceDescriptorsByServiceType(typeof(IBrokeredMessageReceiver<ProcessChangeFeedCommand<FakeSourceEvent>>)));
            Assert.Single(_builder.Services.GetServiceDescriptorsByServiceType(typeof(IBrokeredMessageReceiver<ProcessChangeFeedCommand<AnotherFakeSourceEvent>>)));
        }

        [Fact]
        public void MustThrowWhenASourceEventTypeIsMappedToMoreThanOneTable()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(table: "Event1"), Mapping(RowOperation.Delete, table: "Event2"));

            var e = Assert.Throws<ArgumentException>(() => Use(configuration));
            Assert.Contains("more than one", e.Message);
        }

        [Fact]
        public void MustTreatTableNamesThatDifferOnlyInCaseAsTheSameTable()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(table: "Event1"), Mapping(RowOperation.Delete, table: "EVENT1"));

            Use(configuration);

            Assert.Single(_builder.Services.GetServiceDescriptorsByServiceType(typeof(ServiceBrokerFeed)));
        }

        [Fact]
        public void MustThrowWhenASourceEventTypeIsNotAMessage()
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(sourceEventType: typeof(PlainSourceEvent)));

            var e = Assert.Throws<ArgumentException>(() => Use(configuration));
            Assert.Contains(nameof(IMessage), e.Message);
        }

        [Fact]
        public void MustThrowIfNullConnectionStringIsSupplied()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithNullConnectionString()
                .WithMappings(Mapping(connectionString: null));

            Assert.Throws<ArgumentNullException>(() => Use(mappings));
        }

        [Fact]
        public void MustThrowIfEmptyConnectionStringIsSupplied()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(string.Empty)
                .WithMappings(Mapping(connectionString: string.Empty));

            Assert.Throws<ArgumentNullException>(() => Use(mappings));
        }

        [Fact]
        public void MustThrowIfNullDatabaseNameIsSupplied()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString("Data Source=localhost;Integrated Security=True;")
                .WithMappings(Mapping(database: null));

            Assert.Throws<ArgumentNullException>(() => Use(mappings));
        }

        [Fact]
        public void MustThrowIfEmptyDatabaseNameIsSupplied()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithMappings(Mapping(connectionString: "Data Source=localhost;Database=;Integrated Security=True;", database: string.Empty));

            Assert.Throws<ArgumentNullException>(() => Use(mappings));
        }

        [Fact]
        public void MustThrowIfEventMappingConfigurationItemConnectionStringNotCorrectFormat()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithNullConnectionString()
                .WithMappings(Mapping(connectionString: "Not a valid conn str", database: null));

            Assert.Throws<ArgumentException>(() => Use(mappings));
        }

        [Fact]
        public void MustThrowIfEventMappingConfigurationConnectionStringNotCorrectFormat()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString("Not a valid conn str")
                .WithMappings(Mapping(database: null));

            Assert.Throws<ArgumentException>(() => Use(mappings));
        }

        [Fact]
        public void MustPreferEventMappingConfigurationItemConnectionString()
        {
            var itemConnStr = "Data Source=eventMapping;Integrated Security=True;";
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(connectionString: itemConnStr));

            Use(mappings);

            Assert.Equal(itemConnStr, _builder.Services.BuildServiceProvider().GetRequiredService<SqlServiceBrokerOptions>().ConnectionString);
        }

        [Fact]
        public void MustUseTheSourcesConnectionStringBeforeTheRelays()
        {
            var sourceConnStr = "Data Source=source;Integrated Security=True;";
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithSource("fakedb-ssb", "ServiceBroker", sourceConnStr)
                .WithMappings(Mapping(source: "fakedb-ssb"));

            Use(mappings);

            Assert.Equal(sourceConnStr, _builder.Services.BuildServiceProvider().GetRequiredService<SqlServiceBrokerOptions>().ConnectionString);
        }

        [Fact]
        public void MustUseEventMappingConfigurationConnStrIfEventMappingConfigurationItemConnStrIsWhitespace()
        {
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(ConnectionString)
                .WithMappings(Mapping(connectionString: " "));

            Use(mappings);

            Assert.Equal(ConnectionString, _builder.Services.BuildServiceProvider().GetRequiredService<SqlServiceBrokerOptions>().ConnectionString);
        }

        [Fact]
        public void MustUseTheConnectionStringsDatabaseIfTheMappingDoesNotNameOne()
        {
            Use(Context.Configuration().EventMappingConfiguration.WithConnectionString(ConnectionString).WithMappings(Mapping(database: null)));

            var manager = (SqlDependencyManager<FakeSourceEvent>)_builder.Services.BuildServiceProvider().GetRequiredService<ISqlDependencyManager<FakeSourceEvent>>();
            Assert.Equal("FakeDb", new SqlConnectionStringBuilder(manager.Options.ConnectionString).InitialCatalog);
            Assert.Equal("FakeDb", manager.Options.DatabaseName);
        }

        [Fact]
        public void MustUseTheMappingsDatabaseIfSpecified()
        {
            Use(Context.Configuration().EventMappingConfiguration.WithConnectionString(ConnectionString).WithMappings(Mapping(database: "ExplicitDb")));

            var manager = (SqlDependencyManager<FakeSourceEvent>)_builder.Services.BuildServiceProvider().GetRequiredService<ISqlDependencyManager<FakeSourceEvent>>();
            Assert.Equal("ExplicitDb", manager.Options.DatabaseName);
        }

        [Fact]
        public void MustReturnTheSameBuilder()
        {
            var relayBuilder = new FakeRelayBuilder(_builder, Context.Configuration().EventMappingConfiguration.Mock);
            Assert.Same(relayBuilder, relayBuilder.UseServiceBrokerChangeSource());
        }

        private void Use(EventMappingConfiguration configuration)
            => new FakeRelayBuilder(_builder, configuration).UseServiceBrokerChangeSource();

        private IChangeSource[] ChangeSources()
            => _builder.Services.BuildServiceProvider().GetServices<IChangeSource>().ToArray();

        private EventMappingConfigurationItem Mapping(
            RowOperation operation = RowOperation.Update,
            string source = null,
            string table = "Event1",
            string database = "FakeDb",
            string connectionString = null,
            Type sourceEventType = null,
            Type integrationEventType = null)
            => Context.Configuration().EventMappingConfigurationItem
                .WithSource(source)
                .WithChangeType(operation)
                .WithTable(table)
                .WithDatabaseName(database)
                .WithConnectionString(connectionString)
                .WithSourceEventType(sourceEventType ?? typeof(FakeSourceEvent))
                .WithIntegrationEventType(integrationEventType ?? typeof(FakeEvent))
                .Mock;

        public class FakeSourceEvent : ISourceEvent { }
        public class AnotherFakeSourceEvent : ISourceEvent { }
        public class PlainSourceEvent { }
        public class FakeEvent : IEvent { }
        public class AnotherFakeEvent : IEvent { }
    }
}
