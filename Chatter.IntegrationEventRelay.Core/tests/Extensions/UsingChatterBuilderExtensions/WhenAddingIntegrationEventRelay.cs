using Chatter.CQRS;
using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Chatter.IntegrationEventRelay.Core.Publishing;
using Chatter.IntegrationEventRelay.Core.Validation;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Extensions.UsingChatterBuilderExtensions
{
    public class WhenAddingIntegrationEventRelay : MockContext
    {
        private readonly IChatterBuilder _builder;

        public WhenAddingIntegrationEventRelay()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeSourceEvent), typeof(FakeEvent), typeof(AnotherFakeSourceEvent), typeof(AnotherFakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            _builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;
        }

        [Fact]
        public void MustRegisterIntegrationEventPublisher()
        {
            _builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.Mock);

            var sd = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IIntegrationEventPublisher));
            Assert.Single(sd);
            Assert.All(sd, sd => Assert.Equal(ServiceLifetime.Scoped, sd.Lifetime));
            Assert.All(sd, sd => Assert.Equal(typeof(BrokeredIntegrationEventPublisher), sd.ImplementationType));
        }

        [Fact]
        public void MustRegisterOpenGenericRowChangeProcessor()
        {
            _builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.Mock);

            var sd = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IRowChangeProcessor<>));
            Assert.Single(sd);
            Assert.All(sd, sd => Assert.Equal(ServiceLifetime.Scoped, sd.Lifetime));
            Assert.All(sd, sd => Assert.Equal(typeof(RowChangeProcessor<>), sd.ImplementationType));
        }

        [Fact]
        public void MustRegisterStartupValidation()
        {
            _builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.Mock);

            Assert.Contains(_builder.Services, sd => sd.ServiceType == typeof(IRelayStartupCheck) && sd.ImplementationType == typeof(MappingStartupCheck));
            Assert.Contains(_builder.Services, sd => sd.ServiceType == typeof(IHostedService) && sd.ImplementationType == typeof(StartupValidationService));
        }

        [Fact]
        public void MustReturnBuilderForTheSameConfiguration()
        {
            var mappings = Context.Configuration().EventMappingConfiguration.Mock;
            var relayBuilder = _builder.AddIntegrationEventRelay(mappings);

            Assert.Same(mappings, relayBuilder.Configuration);
            Assert.Same(_builder, relayBuilder.ChatterBuilder);
            Assert.Same(_builder.Services, relayBuilder.Services);
        }

        [Fact]
        public void MustRegisterOneMappingInvokerPerSourceAndIntegrationEventType()
        {
            var update = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(RowOperation.Update)
                .Mock;
            var delete = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(RowOperation.Delete)
                .Mock;
            var other = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(AnotherFakeEvent))
                .WithChangeType(RowOperation.Update)
                .Mock;

            _builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.WithMappings(update, delete, other));

            var sd = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IMappingInvoker<FakeSourceEvent>));
            Assert.Equal(2, sd.Count);
            Assert.All(sd, sd => Assert.Equal(ServiceLifetime.Scoped, sd.Lifetime));
            Assert.Contains(sd, sd => sd.ImplementationType == typeof(MappingInvoker<FakeSourceEvent, FakeEvent>));
            Assert.Contains(sd, sd => sd.ImplementationType == typeof(MappingInvoker<FakeSourceEvent, AnotherFakeEvent>));
        }

        [Fact]
        public void MustRegisterMapperOnceWhenMappingsShareMapperInterface()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper), typeof(FakeSourceEvent), typeof(FakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(RowOperation.Update)
                .Mock;
            var sameMapper = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(RowOperation.Update)
                .WithTable("another-table")
                .Mock;

            builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.WithMappings(item, sameMapper));

            Assert.Single(builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>)));
        }

        [Fact]
        public void MustThrowIfIntegrationEventTypeIsNotAnEvent()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(AnotherFakeSourceEvent));
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithMappings(item);
            Assert.Throws<ArgumentException>(() => _builder.AddIntegrationEventRelay(mappings));
        }

        [Fact]
        public void MustRegisterEventMappingConfiguration()
        {
            var mappings = Context.Configuration().EventMappingConfiguration.Mock;
            _builder.AddIntegrationEventRelay(mappings);

            var sd = _builder.Services.GetServiceDescriptorsByServiceType(typeof(EventMappingConfiguration));
            Assert.All(sd, sd => Assert.Equal(ServiceLifetime.Singleton, sd.Lifetime));
            Assert.All(sd, sd => Assert.Equal(mappings, sd.ImplementationInstance));
            Assert.All(sd, sd => Assert.Equal(typeof(EventMappingConfiguration), sd.ServiceType));
        }

        [Fact]
        public void MustThrowIfAnyMappingsHaveNullSourceEventType()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithIntegrationEventType(typeof(FakeEvent));
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithMappings(item);
            Assert.Throws<ArgumentNullException>(() => _builder.AddIntegrationEventRelay(mappings));
        }

        [Fact]
        public void MustThrowIfAntMappingsHaveNullIntegrationEventType()
        {
            var item = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(FakeEvent));
            var mappings = Context.Configuration().EventMappingConfiguration
                .WithMappings(item);
            Assert.Throws<ArgumentNullException>(() => _builder.AddIntegrationEventRelay(mappings));
        }

        [Fact]
        public void MustNotRegisterMapperIfNoMappingsExistWithMatchingChangeType()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper), typeof(FakeSourceEvent), typeof(FakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithChangeType(RowOperation.Insert)
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(FakeSourceEvent))
                 .WithIntegrationEventType(typeof(FakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1);

            builder.AddIntegrationEventRelay(mappings);

            var updateMappers = builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>));

            Assert.Empty(updateMappers);
        }

        [Fact]
        public void MustNotRegisterMapperIfNoMappingsExistWithMatchingSourceEventType()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper), typeof(AnotherFakeSourceEvent), typeof(FakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithChangeType(RowOperation.Update)
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(AnotherFakeSourceEvent))
                 .WithIntegrationEventType(typeof(FakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1);

            builder.AddIntegrationEventRelay(mappings);

            var updateMappers = builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>));

            Assert.Empty(updateMappers);
        }

        [Fact]
        public void MustNotRegisterMapperIfNoMappingsExistWithMatchingIntegrationEventType()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper), typeof(FakeSourceEvent), typeof(AnotherFakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithChangeType(RowOperation.Update)
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(FakeSourceEvent))
                 .WithIntegrationEventType(typeof(AnotherFakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1);

            builder.AddIntegrationEventRelay(mappings);

            var updateMappers = builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>));

            Assert.Empty(updateMappers);
        }

        [Fact]
        public void MustRegisterAllRowUpdateMappers()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper), typeof(FakeSourceEvent), typeof(FakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithChangeType(RowOperation.Update)
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(FakeSourceEvent))
                 .WithIntegrationEventType(typeof(FakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1);

            builder.AddIntegrationEventRelay(mappings);

            var updateMappers = builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>));

            Assert.All(updateMappers, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(updateMappers, sd => Assert.Equal(typeof(FakeMapper), sd?.ImplementationType));
            Assert.Contains(updateMappers, sd => sd?.ServiceType == typeof(IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>));
            Assert.DoesNotContain(updateMappers, sd => sd.ImplementationType == typeof(FakeMapper2));
            Assert.DoesNotContain(updateMappers, sd => sd.ImplementationType == typeof(FakeMapper3));
            Assert.Single(updateMappers);
        }

        [Fact]
        public void MustRegisterAllRowDeleteMappers()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper2), typeof(AnotherFakeSourceEvent), typeof(AnotherFakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithChangeType(RowOperation.Delete)
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(AnotherFakeSourceEvent))
                 .WithIntegrationEventType(typeof(AnotherFakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1);

            builder.AddIntegrationEventRelay(mappings);

            var deleteMappers = builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceDeleteToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent>));

            Assert.All(deleteMappers, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(deleteMappers, sd => Assert.Equal(typeof(FakeMapper2), sd?.ImplementationType));
            Assert.Contains(deleteMappers, sd => sd?.ServiceType == typeof(IMapSourceDeleteToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent>));
            Assert.DoesNotContain(deleteMappers, sd => sd.ImplementationType == typeof(FakeMapper));
            Assert.DoesNotContain(deleteMappers, sd => sd.ImplementationType == typeof(FakeMapper3));
            Assert.Single(deleteMappers);
        }

        [Fact]
        public void MustRegisterAllRowInsertMappers()
        {
            var assembly = Context.Common().Assembly
                .WithTypes(typeof(FakeMapper3), typeof(AnotherFakeSourceEvent), typeof(AnotherFakeEvent)).Mock;
            var assemblySourceFilter = Context.Cqrs().AssemblySourceFilter
                .WithAssemblySource(assembly).Mock;
            var builder = Context.Cqrs().ChatterBuilder
                .WithAssemblySourceFilter(assemblySourceFilter).Mock;

            var item1 = Context.Configuration().EventMappingConfigurationItem
                 .WithNullConnectionString()
                 .WithChangeType(RowOperation.Insert)
                 .WithDatabaseName("FakeDb2")
                 .WithTable("dbo.FakeTable2")
                 .WithSourceEventType(typeof(AnotherFakeSourceEvent))
                 .WithIntegrationEventType(typeof(AnotherFakeEvent))
                 .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1).Mock;

            builder.AddIntegrationEventRelay(mappings);

            var insertMappers = builder.Services.GetServiceDescriptorsByServiceType(typeof(IMapSourceInsertToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent>));

            Assert.All(insertMappers, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(insertMappers, sd => Assert.Equal(typeof(FakeMapper3), sd?.ImplementationType));
            Assert.Contains(insertMappers, sd => sd?.ServiceType == typeof(IMapSourceInsertToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent>));
            Assert.DoesNotContain(insertMappers, sd => sd.ImplementationType == typeof(FakeMapper2));
            Assert.DoesNotContain(insertMappers, sd => sd.ImplementationType == typeof(FakeMapper));
            Assert.Single(insertMappers);
        }

        private class FakeSourceEvent : ISourceEvent { }
        private class AnotherFakeSourceEvent : ISourceEvent { }
        private class FakeEvent : IEvent { }
        private class AnotherFakeEvent : IEvent { }
        private class FakeMapper : IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent?>
        {
            public Task<FakeEvent?> MapAsync(MappingData<FakeSourceEvent> mappingData, RelayContext context, EventMappingConfigurationItem? mappingConfig)
            {
                throw new NotImplementedException();
            }
        }
        private class FakeMapper2 : IMapSourceDeleteToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent?>
        {
            public Task<AnotherFakeEvent?> MapAsync(MappingData<AnotherFakeSourceEvent> mappingData, RelayContext context, EventMappingConfigurationItem? mappingConfig)
            {
                throw new NotImplementedException();
            }
        }
        private class FakeMapper3 : IMapSourceInsertToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent?>
        {
            public Task<AnotherFakeEvent?> MapAsync(MappingData<AnotherFakeSourceEvent> mappingData, RelayContext context, EventMappingConfigurationItem? mappingConfig)
            {
                throw new NotImplementedException();
            }
        }
    }
}
