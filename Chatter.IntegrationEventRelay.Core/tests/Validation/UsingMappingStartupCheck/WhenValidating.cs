using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Validation.UsingMappingStartupCheck
{
    public class WhenValidating : MockContext
    {
        private readonly ServiceCollection _services = new();
        private readonly MappingStartupCheck _sut = new();

        [Fact]
        public void MustPassAValidConfiguration()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource("ssb", ChangeSourceCapabilities.None));
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();

            Assert.Empty(Validate(Mapping("ssb")));
        }

        [Fact]
        public void MustPassAMappingWithoutSourceWhenTheDefaultSourceIsRegistered()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource(EventMappingConfiguration.DefaultSourceName, ChangeSourceCapabilities.None));
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();

            Assert.Empty(Validate(Mapping(null)));
        }

        [Fact]
        public void MustFailWhenTheSourceIsNotConfigured()
        {
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();

            var problem = Assert.Single(Validate(Mapping("missing")));
            Assert.Contains("'missing'", problem);
            Assert.Contains("isn't configured", problem);
        }

        [Fact]
        public void MustFailWhenNoAdapterIsRegisteredForTheSource()
        {
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();

            var problem = Assert.Single(Validate(Mapping("cdc")));
            Assert.Contains("no change source adapter", problem);
        }

        [Fact]
        public void MustFailWhenTheRegisteredSourceWithThatNameIsOfAnotherType()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource("cdc", ChangeSourceCapabilities.None, "ServiceBroker"));
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();

            var problem = Assert.Single(Validate(Mapping("cdc")));
            Assert.Contains("no change source adapter", problem);
        }

        [Fact]
        public void MustFailWhenTheSourceLacksARequiredCapability()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource("ssb", ChangeSourceCapabilities.CommitTime));
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();
            var mapping = Mapping("ssb");
            mapping.RequiredCapabilities = ChangeSourceCapabilities.Ordered | ChangeSourceCapabilities.CommitTime;

            var problem = Assert.Single(Validate(mapping));
            Assert.Contains("'Ordered'", problem);
        }

        [Fact]
        public void MustPassWhenTheSourceProvidesTheRequiredCapabilities()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource("cdc", ChangeSourceCapabilities.Ordered | ChangeSourceCapabilities.Resumable, "Cdc"));
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();
            var mapping = Mapping("cdc");
            mapping.RequiredCapabilities = ChangeSourceCapabilities.Ordered;

            Assert.Empty(Validate(mapping));
        }

        [Fact]
        public void MustFailWhenThereIsNoMapper()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource("ssb", ChangeSourceCapabilities.None));

            var problem = Assert.Single(Validate(Mapping("ssb")));
            Assert.Contains("IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>", problem);
            Assert.Contains("found 0", problem);
        }

        [Fact]
        public void MustFailWhenThereIsMoreThanOneMapper()
        {
            _services.AddSingleton<IChangeSource>(new FakeChangeSource("ssb", ChangeSourceCapabilities.None));
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, FakeMapper>();
            _services.AddTransient<IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>, OtherFakeMapper>();

            var problem = Assert.Single(Validate(Mapping("ssb")));
            Assert.Contains("found 2", problem);
        }

        [Fact]
        public void MustReportEveryProblem()
        {
            Assert.Equal(2, Validate(Mapping("missing"), Mapping("cdc")).Count);
        }

        private System.Collections.Generic.List<string> Validate(params EventMappingConfigurationItem[] mappings)
        {
            var configuration = Context.Configuration().EventMappingConfiguration
                .WithSource("ssb", "ServiceBroker")
                .WithSource("cdc", "Cdc")
                .WithMappings(mappings)
                .Mock;
            _services.AddSingleton(configuration);
            return _sut.Validate(_services.BuildServiceProvider()).ToList();
        }

        private EventMappingConfigurationItem Mapping(string source)
            => Context.Configuration().EventMappingConfigurationItem
                .WithSource(source)
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(RowOperation.Update)
                .Mock;

        private sealed record FakeChangeSource(string Name, ChangeSourceCapabilities Capabilities, string Type = "ServiceBroker") : IChangeSource;

        public class FakeSourceEvent { }
        public class FakeEvent : IEvent { }
        private class FakeMapper : IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>
        {
            public Task<FakeEvent> MapAsync(MappingData<FakeSourceEvent> mappingData, RelayContext context, EventMappingConfigurationItem mappingConfig) => Task.FromResult<FakeEvent>(null);
        }
        private class OtherFakeMapper : IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>
        {
            public Task<FakeEvent> MapAsync(MappingData<FakeSourceEvent> mappingData, RelayContext context, EventMappingConfigurationItem mappingConfig) => Task.FromResult<FakeEvent>(null);
        }
    }
}
