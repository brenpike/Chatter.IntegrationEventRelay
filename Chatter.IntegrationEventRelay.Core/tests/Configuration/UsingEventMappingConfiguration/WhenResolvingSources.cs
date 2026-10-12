using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using System.Linq;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Configuration.UsingEventMappingConfiguration
{
    public class WhenResolvingSources : MockContext
    {
        [Fact]
        public void MustUseTheDefaultSourceForMappingsWithoutOne()
        {
            var mapping = Context.Configuration().EventMappingConfigurationItem.WithSource(null).Mock;
            Assert.Equal(EventMappingConfiguration.DefaultSourceName, EventMappingConfiguration.GetSourceName(mapping));
        }

        [Fact]
        public void MustUseTheMappingsSource()
        {
            var mapping = Context.Configuration().EventMappingConfigurationItem.WithSource("fakedb-cdc").Mock;
            Assert.Equal("fakedb-cdc", EventMappingConfiguration.GetSourceName(mapping));
        }

        [Fact]
        public void MustResolveTheDefaultSourceToServiceBrokerWhenNotConfigured()
        {
            var configuration = Context.Configuration().EventMappingConfiguration.Mock;
            Assert.Equal(EventMappingConfiguration.DefaultSourceType, configuration.GetSourceType(EventMappingConfiguration.DefaultSourceName));
        }

        [Fact]
        public void MustResolveAConfiguredSourcesType()
        {
            var configuration = Context.Configuration().EventMappingConfiguration.WithSource("fakedb-cdc", "Cdc").Mock;
            Assert.Equal("Cdc", configuration.GetSourceType("fakedb-cdc"));
        }

        [Fact]
        public void MustResolveSourceNamesIgnoringCase()
        {
            var configuration = Context.Configuration().EventMappingConfiguration.WithSource("fakedb-cdc", "Cdc").Mock;
            Assert.Equal("Cdc", configuration.GetSourceType("FAKEDB-CDC"));
        }

        [Fact]
        public void MustLetConfigurationOverrideTheDefaultSource()
        {
            var configuration = Context.Configuration().EventMappingConfiguration.WithSource(EventMappingConfiguration.DefaultSourceName, "Cdc").Mock;
            Assert.Equal("Cdc", configuration.GetSourceType(EventMappingConfiguration.DefaultSourceName));
        }

        [Fact]
        public void MustReturnNullForAnUnknownSource()
        {
            var configuration = Context.Configuration().EventMappingConfiguration.Mock;
            Assert.Null(configuration.GetSourceType("unknown"));
        }

        [Fact]
        public void MustReturnMappingsForASourceType()
        {
            var legacy = Context.Configuration().EventMappingConfigurationItem.WithSource(null).Mock;
            var ssb = Context.Configuration().EventMappingConfigurationItem.WithSource("fakedb-ssb").Mock;
            var cdc = Context.Configuration().EventMappingConfigurationItem.WithSource("fakedb-cdc").Mock;
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithSource("fakedb-ssb", "ServiceBroker")
                .WithSource("fakedb-cdc", "Cdc")
                .WithMappings(legacy, ssb, cdc)
                .Mock;

            Assert.Equal(new[] { legacy, ssb }, configuration.GetMappingsForSourceType("ServiceBroker").ToArray());
            Assert.Equal(new[] { cdc }, configuration.GetMappingsForSourceType("Cdc").ToArray());
        }

        [Fact]
        public void MustPreferTheMappingsConnectionString()
        {
            var mapping = Context.Configuration().EventMappingConfigurationItem.WithSource("src").WithConnectionString("mapping").Mock;
            var configuration = Context.Configuration().EventMappingConfiguration.WithConnectionString("relay").WithSource("src", "ServiceBroker", "source").Mock;
            Assert.Equal("mapping", configuration.GetConnectionString(mapping));
        }

        [Fact]
        public void MustFallBackToTheSourcesConnectionString()
        {
            var mapping = Context.Configuration().EventMappingConfigurationItem.WithSource("src").WithNullConnectionString().Mock;
            var configuration = Context.Configuration().EventMappingConfiguration.WithConnectionString("relay").WithSource("src", "ServiceBroker", "source").Mock;
            Assert.Equal("source", configuration.GetConnectionString(mapping));
        }

        [Fact]
        public void MustFallBackToTheRelaysConnectionString()
        {
            var mapping = Context.Configuration().EventMappingConfigurationItem.WithSource(null).WithNullConnectionString().Mock;
            var configuration = Context.Configuration().EventMappingConfiguration.WithConnectionString("relay").Mock;
            Assert.Equal("relay", configuration.GetConnectionString(mapping));
        }
    }
}
