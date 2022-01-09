using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Extensions.UsingChatterBuilderExtensions
{
    public class WhenWiringSourceToIntegrationEventMappers : MockContext
    {
        [Fact]
        public void MustWireMappersForSpecifiedMapperType()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), item1);

            Assert.All(builder.Services, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(builder.Services, sd => Assert.Equal(typeof(FakeMapper), sd?.ImplementationType));
            Assert.Contains(builder.Services, sd => sd?.ServiceType == typeof(IMapSourceUpdateToIntegrationEvent<FakeSource, FakeEvent>));
            Assert.Contains(builder.Services, sd => sd?.ServiceType == typeof(IMapSourceToIntegrationEvent<FakeSource, FakeEvent>));
            Assert.DoesNotContain(builder.Services, sd => sd.ImplementationType == typeof(FakeMapper2));
            Assert.Equal(2, builder.Services.Count);
        }

        [Fact]
        public void MustThrowIfAssemblySourceFilterIsNull()
        {
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(null).Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), Context.Configuration().EventMappingConfigurationItem.Mock));
        }

        [Fact]
        public void MustNotWireAnyMappersIfNoMapperTypesExistInAssembly()
        {
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter.Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), item1);

            Assert.Empty(builder.Services);
        }

        [Fact]
        public void MustThrowIfSourceEventTypeIsNull()
        {
            var assembly1 = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly1).Mock;

            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), item1));
        }

        [Fact]
        public void MustThrowIfIntegrationEventTypeIsNull()
        {
            var assembly1 = Context.Common().Assembly
               .WithTypes(typeof(FakeMapper)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly1).Mock;

            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(FakeSource))
                .Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), item1));
        }

        [Fact]
        public void MustThrowIfEventMappingConfigurationItemIsNull()
        {
            var assembly1 = Context.Common().Assembly
               .WithTypes(typeof(FakeMapper)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly1).Mock;

            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), null));
        }

        [Fact]
        public void MustAppendDuplicateServiceTypesToServiceCollection()
        {
            var assembly1 = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper)).Mock;
            var assembly2 = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly1, assembly2).Mock;

            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceUpdateToIntegrationEvent<,>), item1);

            Assert.All(builder.Services, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(builder.Services, sd => Assert.Equal(typeof(FakeMapper), sd?.ImplementationType));
            Assert.Contains(builder.Services, sd => sd?.ServiceType == typeof(IMapSourceUpdateToIntegrationEvent<FakeSource, FakeEvent>));
            Assert.Contains(builder.Services, sd => sd?.ServiceType == typeof(IMapSourceToIntegrationEvent<FakeSource, FakeEvent>));
            Assert.DoesNotContain(builder.Services, sd => sd.ImplementationType == typeof(FakeMapper2));
            Assert.Equal(4, builder.Services.Count);
        }

        [Fact]
        public void MustAppendDuplicateImplementationTypesToServiceCollection()
        {
            var assembly1 = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper2)).Mock;
            var assembly2 = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper3)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly1, assembly2).Mock;

            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceDeleteToIntegrationEvent<,>), item1);

            Assert.All(builder.Services, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.Contains(builder.Services, sd => typeof(FakeMapper2) == sd?.ImplementationType);
            Assert.Contains(builder.Services, sd => typeof(FakeMapper3) == sd?.ImplementationType);
            Assert.Contains(builder.Services, sd => sd?.ServiceType == typeof(IMapSourceDeleteToIntegrationEvent<FakeSource, FakeEvent>));
            Assert.Contains(builder.Services, sd => sd?.ServiceType == typeof(IMapSourceToIntegrationEvent<FakeSource, FakeEvent>));
            Assert.Equal(4, builder.Services.Count);
        }

        [Fact]
        public void MustNotRegisterMappersForImplementationTypesThatDoNotMatch()
        {
            var assembly1 = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly1).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(FakeSource))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            builder.WireSourceToIntegrationEventMappers(typeof(IMapSourceDeleteToIntegrationEvent<,>), item1);

            Assert.Empty(builder.Services);
        }

        private class FakeSource : ISourceEvent { }
        private class FakeEvent : IEvent { }
        private class FakeMapper : IMapSourceUpdateToIntegrationEvent<FakeSource, FakeEvent>
        {
            public Task<FakeEvent> MapAsync(MappingData<FakeSource> mappingData)
            {
                throw new NotImplementedException();
            }
        }
        private class FakeMapper2 : IMapSourceDeleteToIntegrationEvent<FakeSource, FakeEvent>
        {
            public Task<FakeEvent> MapAsync(MappingData<FakeSource> mappingData)
            {
                throw new NotImplementedException();
            }
        }
        private class FakeMapper3 : IMapSourceDeleteToIntegrationEvent<FakeSource, FakeEvent>
        {
            public Task<FakeEvent> MapAsync(MappingData<FakeSource> mappingData)
            {
                throw new NotImplementedException();
            }
        }
    }
}
