using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.MessageBrokers.Receiving;
using Chatter.MessageBrokers.SqlServiceBroker.Configuration;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Data.SqlClient;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Extensions.UsingChatterBuilderExtensions
{
    public class WhenAddingSqlChangeFeedPerUniqueDataSource : MockContext
    {
        private readonly IChatterBuilder _builder;

        public WhenAddingSqlChangeFeedPerUniqueDataSource()
        {
            var assembly = Context.Common().Assembly.WithTypes(typeof(FakeEvent), typeof(AnotherFakeEvent)).Mock;
            var asf = Context.Cqrs().AssemblySourceFilter.WithAssemblySource(assembly).Mock;
            _builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(asf)
                .Mock;
        }

        [Fact]
        public void MustThrowIfNullConnectionStringIsSupplied()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithNullConnectionString()
                .WithMappings(item)
                .Mock;

            Assert.Throws<ArgumentNullException>(() => _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null));
        }

        [Fact]
        public void MustThrowIfNullDatabaseNameIsSupplied()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithNullDatabaseName()
                .WithNullConnectionString()
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString("Data Source=localhost;Integrated Security=True;")
                .WithMappings(item)
                .Mock;

            Assert.Throws<ArgumentNullException>(() => _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null));
        }

        [Fact]
        public void MustThrowIfEmptyConnectionStringIsSupplied()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithConnectionString(string.Empty)
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(string.Empty)
                .WithMappings(item)
                .Mock;

            Assert.Throws<ArgumentNullException>(() => _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null));
        }

        [Fact]
        public void MustThrowIfEmptyDatabaseNameIsSupplied()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithDatabaseName(string.Empty)
                .WithConnectionString("Data Source=localhost;Database=;Integrated Security=True;")
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString("Data Source=localhost;Database=;Integrated Security=True;")
                .WithMappings(item)
                .Mock;

            Assert.Throws<ArgumentNullException>(() => _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null));
        }

        [Fact]
        public void MustThrowIfEventMappingConfigurationItemConnectionStringNotCorrectFormat()
        {
            var items = Context.Configuration().EventMappingConfigurationItem
                .WithConnectionString("Not a valid conn str")
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithNullConnectionString()
                .WithMappings(items)
                .Mock;

            Assert.Throws<ArgumentException>(() => _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null));
        }

        [Fact]
        public void MustThrowIfEventMappingConfigurationConnectionStringNotCorrectFormat()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString("Not a valid conn str")
                .WithMappings(item)
                .Mock;

            Assert.Throws<ArgumentException>(() => _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null));
        }

        [Fact]
        public void MustPreferEventMappingConfigurationItemConnectionString()
        {
            var eventMappingConfigItemConnStr = "Data Source=eventMapping;Integrated Security=True;";
            var eventMappingConfigConnStr = "Data Source=sourceToIntegrationEvent;Integrated Security=True;";
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithConnectionString(eventMappingConfigItemConnStr)
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(eventMappingConfigConnStr)
                .WithMappings(item)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var sp = _builder.Services.BuildServiceProvider();
            var opts = sp.GetRequiredService<SqlServiceBrokerOptions>();

            Assert.Equal(eventMappingConfigItemConnStr, opts.ConnectionString);
            Assert.NotEqual(eventMappingConfigConnStr, opts.ConnectionString);
        }

        [Fact]
        public void MustUseEventMappingConfigurationConnStrIfEventMappingConfigurationItemConnStrIsNull()
        {
            var eventMappingConfigurationConnStr = "Data Source=sourceToIntegrationEvent;Integrated Security=True;";
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(eventMappingConfigurationConnStr)
                .WithMappings(item)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var sp = _builder.Services.BuildServiceProvider();
            var opts = sp.GetRequiredService<SqlServiceBrokerOptions>();

            Assert.Equal(eventMappingConfigurationConnStr, opts.ConnectionString);
        }

        [Fact]
        public void MustEventMappingConfigurationConnStrIfEventMappingConfigurationItemConnStrIsWhitespace()
        {
            var sourceToIntEventConnStr = "Data Source=sourceToIntegrationEvent;Integrated Security=True;";
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithConnectionString(string.Empty)
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(sourceToIntEventConnStr)
                .WithMappings(item)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var sp = _builder.Services.BuildServiceProvider();
            var opts = sp.GetRequiredService<SqlServiceBrokerOptions>();

            Assert.Equal(sourceToIntEventConnStr, opts.ConnectionString);
        }

        [Fact]
        public void MustUseEventMappingConfigurationConnStrDatabaseIfEventMappingConfigurationItemDatabaseAndConnStrAreNotSet()
        {
            var databaseName = "DbFromConnStr";
            var eventMappingConfigConnStr = $"Data Source=sourceToIntegrationEvent;Database={databaseName};Integrated Security=True;";
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithNullDatabaseName()
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(eventMappingConfigConnStr)
                .WithMappings(item)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var sp = _builder.Services.BuildServiceProvider();
            var opts = (SqlDependencyManager<FakeEvent>)sp.GetRequiredService<ISqlDependencyManager<FakeEvent>>();
            var connStrBuilder = new SqlConnectionStringBuilder(opts.Options.ConnectionString);

            Assert.Equal(databaseName, connStrBuilder.InitialCatalog);
        }

        [Fact]
        public void MustUseEventMappingConfigurationItemDatabaseIfSpecified()
        {
            var databaseNameFromConnStr = "DbFromConnStr";
            var explicitDatabase = "DbNameSetExplicitlyOnEventMappingConfigItem";
            var eventMappingConfigConnStr = $"Data Source=sourceToIntegrationEvent;Database={databaseNameFromConnStr};Integrated Security=True;";
            var items = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName(explicitDatabase)
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(eventMappingConfigConnStr)
                .WithMappings(items)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var sp = _builder.Services.BuildServiceProvider();
            var opts = (SqlDependencyManager<FakeEvent>)sp.GetRequiredService<ISqlDependencyManager<FakeEvent>>();

            Assert.Equal(explicitDatabase, opts.Options.DatabaseName);
            Assert.NotEqual(databaseNameFromConnStr, opts.Options.DatabaseName);
        }

        [Fact]
        public void MustAddSqlChangeFeedForEachUniqueMappingSource()
        {
            var sourceToIntEventConnStr = $"Data Source=sourceToIntegrationEvent;Database=;Integrated Security=True;";
            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb1")
                .WithTable("dbo.FakeTable")
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;

            var item2 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(AnotherFakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(sourceToIntEventConnStr)
                .WithMappings(item1, item2)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var anotherFakeEventReceivers = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IBrokeredMessageReceiver<ProcessChangeFeedCommand<AnotherFakeEvent>>));
            var fakeEventReceivers = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IBrokeredMessageReceiver<ProcessChangeFeedCommand<FakeEvent>>));

            Assert.Single(anotherFakeEventReceivers);
            Assert.Single(fakeEventReceivers);
        }

        [Fact]
        public void MustNotAddDuplicateSqlChangeFeeds()
        {
            var sourceToIntEventConnStr = $"Data Source=sourceToIntegrationEvent;Database=;Integrated Security=True;";
            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb1")
                .WithTable("dbo.FakeTable")
                .WithSourceEventType(typeof(FakeEvent))
                .Mock;

            var item2 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithDatabaseName("FakeDb1")
                 .WithTable("dbo.FakeTable")
                 .WithSourceEventType(typeof(FakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString(sourceToIntEventConnStr)
                .WithMappings(item1, item2)
                .Mock;

            _builder.AddSqlChangeFeedPerUniqueDataSource(mappings, null);

            var fakeEventReceivers = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IBrokeredMessageReceiver<ProcessChangeFeedCommand<FakeEvent>>));

            Assert.Single(fakeEventReceivers);
        }

        private class FakeEvent : IEvent { }
        private class AnotherFakeEvent : IEvent { }
    }
}
