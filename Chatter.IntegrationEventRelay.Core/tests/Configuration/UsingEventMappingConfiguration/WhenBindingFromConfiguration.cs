using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Configuration.UsingEventMappingConfiguration
{
    public class WhenBindingFromConfiguration : MockContext
    {
        // An unchanged 0.3 configuration: no sources, a legacy connection string, short and full type names
        private const string LegacyJson = @"{
  ""IntegrationEventRelay"": {
    ""ConnectionString"": ""Server=db;Database=FakeDb"",
    ""Mappings"": [
      {
        ""SourceEventTypeName"": ""FakeSourceEvent"",
        ""IntegrationEventTypeName"": ""Chatter.IntegrationEventRelay.Core.Tests.Configuration.UsingEventMappingConfiguration.WhenBindingFromConfiguration+FakeEvent"",
        ""SourceChangeType"": ""Update"",
        ""DatabaseName"": """",
        ""TableName"": ""Event1""
      },
      {
        ""SourceEventTypeName"": ""FakeSourceEvent"",
        ""IntegrationEventTypeName"": ""FakeEvent"",
        ""SourceChangeType"": ""Delete"",
        ""TableName"": ""Event1"",
        ""MessagingInfrastructureType"": ""Chatter.Infrastructure.AzureServiceBus"",
        ""InfrastructureMessageName"": ""event-1-deleted""
      }
    ]
  }
}";

        private const string SourcesJson = @"{
  ""Relay"": {
    ""ConnectionString"": ""Server=db;Database=FakeDb"",
    ""Schema"": ""relay"",
    ""Sources"": {
      ""fakedb-cdc"": { ""Type"": ""Cdc"", ""ConnectionString"": ""Server=cdc;Database=FakeDb"", ""PollInterval"": ""00:00:01"" }
    },
    ""Mappings"": [
      {
        ""Source"": ""fakedb-cdc"",
        ""SourceEventTypeName"": ""FakeSourceEvent"",
        ""IntegrationEventTypeName"": ""FakeEvent"",
        ""SourceChangeType"": ""Insert"",
        ""TableName"": ""Event2"",
        ""RequiredCapabilities"": ""Ordered, Resumable"",
        ""CaptureInstance"": ""dbo_Event2""
      }
    ]
  }
}";

        [Fact]
        public void MustBindALegacyConfigurationUnchanged()
        {
            var relay = CreateBuilder(LegacyJson).AddIntegrationEventRelay();
            var mappings = relay.Configuration.Mappings.ToList();

            Assert.Equal("Server=db;Database=FakeDb", relay.Configuration.ConnectionString);
            Assert.Empty(relay.Configuration.Sources);
            Assert.Equal(2, mappings.Count);
            Assert.All(mappings, m => Assert.Null(m.Source));
            Assert.Equal(RowOperation.Update, mappings[0].SourceChangeType);
            Assert.Equal(RowOperation.Delete, mappings[1].SourceChangeType);
            Assert.All(mappings, m => Assert.Equal(typeof(FakeSourceEvent), m.SourceEventType));
            Assert.All(mappings, m => Assert.Equal(typeof(FakeEvent), m.IntegrationEventType));
            Assert.Equal("event-1-deleted", mappings[1].InfrastructureMessageName);
            Assert.All(mappings, m => Assert.Equal(ChangeSourceCapabilities.None, m.RequiredCapabilities));
            Assert.Equal("Chatter", relay.Configuration.Schema);
        }

        [Fact]
        public void MustBindTheRelaySchema()
            => Assert.Equal("relay", CreateBuilder(SourcesJson).AddIntegrationEventRelay("Relay").Configuration.Schema);

        [Fact]
        public void MustBindEachMappingWithItsSection()
        {
            var legacy = CreateBuilder(LegacyJson).AddIntegrationEventRelay().Configuration.Mappings.ToList();
            var withSources = Assert.Single(CreateBuilder(SourcesJson).AddIntegrationEventRelay("Relay").Configuration.Mappings);

            Assert.Equal("event-1-deleted", legacy[1].Section["InfrastructureMessageName"]);
            Assert.Null(legacy[0].Section["InfrastructureMessageName"]);
            Assert.Equal("dbo_Event2", withSources.Section["CaptureInstance"]);
        }

        [Fact]
        public void MustBindNamedSourcesWithTheirSection()
        {
            var relay = CreateBuilder(SourcesJson).AddIntegrationEventRelay("Relay");
            var source = Assert.Single(relay.Configuration.Sources).Value;

            Assert.Equal("Cdc", source.Type);
            Assert.Equal("Server=cdc;Database=FakeDb", source.ConnectionString);
            Assert.Equal("00:00:01", source.Section["PollInterval"]);
        }

        [Fact]
        public void MustBindTheMappingsSourceAndRequiredCapabilities()
        {
            var relay = CreateBuilder(SourcesJson).AddIntegrationEventRelay("Relay");
            var mapping = Assert.Single(relay.Configuration.Mappings);

            Assert.Equal("fakedb-cdc", mapping.Source);
            Assert.Equal(RowOperation.Insert, mapping.SourceChangeType);
            Assert.Equal(ChangeSourceCapabilities.Ordered | ChangeSourceCapabilities.Resumable, mapping.RequiredCapabilities);
        }

        private IChatterBuilder CreateBuilder(string json)
        {
            var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
            var assembly = Context.Common().Assembly.WithTypes(typeof(FakeSourceEvent), typeof(FakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter.WithAssemblySource(assembly).Mock;
            return Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter)
                .WithConfiguration(configuration)
                .Mock;
        }

        public class FakeSourceEvent : ISourceEvent { }
        public class FakeEvent : IEvent { }
    }
}
