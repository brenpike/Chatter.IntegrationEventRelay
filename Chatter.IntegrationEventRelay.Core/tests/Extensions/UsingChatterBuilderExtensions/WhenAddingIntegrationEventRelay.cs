using Chatter.CQRS;
using Chatter.CQRS.DependencyInjection;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.DependencyInjection;
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
        public void MustRegisterEventMappingConfigProvider()
        {
            _builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.Mock);

            var sd = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IEventMappingConfigItemProvider));
            Assert.All(sd, sd => Assert.Equal(ServiceLifetime.Transient, sd.Lifetime));
            Assert.All(sd, sd => Assert.Equal(typeof(EventMappingConfigItemProvider), sd.ImplementationType));
            Assert.All(sd, sd => Assert.Equal(typeof(IEventMappingConfigItemProvider), sd.ServiceType));
        }

        [Fact]
        public void MustRegisterIntegrationEventRelayService()
        {
            _builder.AddIntegrationEventRelay(Context.Configuration().EventMappingConfiguration.Mock);

            var sd = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IRelayIntegrationEvent));
            Assert.All(sd, sd => Assert.Equal(ServiceLifetime.Transient, sd.Lifetime));
            Assert.All(sd, sd => Assert.Equal(typeof(IntegrationEventRelayService), sd.ImplementationType));
            Assert.All(sd, sd => Assert.Equal(typeof(IRelayIntegrationEvent), sd.ServiceType));
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
        public void MustRegisterRowUpdatedEventHandlerForMappingsWithUpdateChangeType()
        {
            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(AnotherFakeEvent))
                .WithChangeType(ChangeTypes.Update)
                .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Initial Catalog=fakedb;Integrated Security=True;")
                .WithMappings(item1);

            _builder.AddIntegrationEventRelay(mappings);
            var svcs = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowUpdatedEvent<FakeSourceEvent>>));

            Assert.Single(svcs);
            Assert.All(svcs, sd => Assert.Equal(typeof(IMessageHandler<RowUpdatedEvent<FakeSourceEvent>>), sd.ServiceType));
            Assert.All(svcs, sd => Assert.Equal(typeof(RowUpdatedEventHandler<FakeSourceEvent, AnotherFakeEvent>), sd.ImplementationType));
            Assert.All(svcs, sd => Assert.Equal(ServiceLifetime.Transient, sd.Lifetime));
        }

        [Fact]
        public void MustRegisterRowInsertedEventHandlerForMappingsWithInsertChangeType()
        {
            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(ChangeTypes.Insert)
                .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Initial Catalog=fakedb;Integrated Security=True;")
                .WithMappings(item1);

            _builder.AddIntegrationEventRelay(mappings);
            var svcs = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowInsertedEvent<FakeSourceEvent>>));

            Assert.Single(svcs);
            Assert.All(svcs, sd => Assert.Equal(typeof(IMessageHandler<RowInsertedEvent<FakeSourceEvent>>), sd.ServiceType));
            Assert.All(svcs, sd => Assert.Equal(typeof(RowInsertedEventHandler<FakeSourceEvent, FakeEvent>), sd.ImplementationType));
            Assert.All(svcs, sd => Assert.Equal(ServiceLifetime.Transient, sd.Lifetime));
        }

        [Fact]
        public void MustRegisterRowDeletedEventHandlerForMappingsWithDeletedChangeType()
        {
            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .WithChangeType(ChangeTypes.Delete)
                .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Initial Catalog=fakedb;Integrated Security=True;")
                .WithMappings(item1);

            _builder.AddIntegrationEventRelay(mappings);
            var svcs = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IMessageHandler<RowDeletedEvent<FakeSourceEvent>>));

            Assert.Single(svcs);
            Assert.All(svcs, sd => Assert.Equal(typeof(IMessageHandler<RowDeletedEvent<FakeSourceEvent>>), sd.ServiceType));
            Assert.All(svcs, sd => Assert.Equal(typeof(RowDeletedEventHandler<FakeSourceEvent, FakeEvent>), sd.ImplementationType));
            Assert.All(svcs, sd => Assert.Equal(ServiceLifetime.Transient, sd.Lifetime));
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
                 .WithChangeType(ChangeTypes.Insert)
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
                 .WithChangeType(ChangeTypes.Update)
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
                 .WithChangeType(ChangeTypes.Update)
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
                 .WithChangeType(ChangeTypes.Update)
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
                 .WithChangeType(ChangeTypes.Delete)
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
                 .WithChangeType(ChangeTypes.Insert)
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

        [Fact]
        public void MustRegisterRowChangeExecutorForEachMapping()
        {
            var item1 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb1")
                .WithTable("dbo.FakeTable")
                .WithSourceEventType(typeof(FakeSourceEvent))
                .WithIntegrationEventType(typeof(FakeEvent))
                .Mock;

            var item2 = Context.Configuration().EventMappingConfigurationItem
                .WithNullConnectionString()
                .WithDatabaseName("FakeDb2")
                .WithTable("dbo.FakeTable2")
                .WithSourceEventType(typeof(AnotherFakeSourceEvent))
                .WithIntegrationEventType(typeof(AnotherFakeEvent))
                .Mock;

            var mappings = Context.Configuration().EventMappingConfiguration
                .WithConnectionString($"Data Source=sourceToIntegrationEvent;Database=db;Integrated Security=True;")
                .WithMappings(item1, item2);

            _builder.AddIntegrationEventRelay(mappings);

            var svc1 = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IRowChangeHandlerExecutor<FakeSourceEvent, FakeEvent>));
            var svc2 = _builder.Services.GetServiceDescriptorsByServiceType(typeof(IRowChangeHandlerExecutor<AnotherFakeSourceEvent, AnotherFakeEvent>));

            Assert.All(svc1, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(svc1, sd => Assert.Equal(typeof(RowChangeExecutor<FakeSourceEvent, FakeEvent>), sd?.ImplementationType));
            Assert.All(svc1, sd => Assert.Equal(typeof(IRowChangeHandlerExecutor<FakeSourceEvent, FakeEvent>), sd?.ServiceType));

            Assert.All(svc2, sd => Assert.Equal(ServiceLifetime.Transient, sd?.Lifetime));
            Assert.All(svc2, sd => Assert.Equal(typeof(RowChangeExecutor<AnotherFakeSourceEvent, AnotherFakeEvent>), sd?.ImplementationType));
            Assert.All(svc2, sd => Assert.Equal(typeof(IRowChangeHandlerExecutor<AnotherFakeSourceEvent, AnotherFakeEvent>), sd?.ServiceType));
        }

        private class FakeSourceEvent : ISourceEvent { }
        private class AnotherFakeSourceEvent : ISourceEvent { }
        private class FakeEvent : IEvent { }
        private class AnotherFakeEvent : IEvent { }
        private class FakeMapper : IMapSourceUpdateToIntegrationEvent<FakeSourceEvent, FakeEvent>
        {
            public Task<FakeEvent> MapAsync(MappingData<FakeSourceEvent> mappingData)
            {
                throw new NotImplementedException();
            }
        }
        private class FakeMapper2 : IMapSourceDeleteToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent>
        {
            public Task<AnotherFakeEvent> MapAsync(MappingData<AnotherFakeSourceEvent> mappingData)
            {
                throw new NotImplementedException();
            }
        }
        private class FakeMapper3 : IMapSourceInsertToIntegrationEvent<AnotherFakeSourceEvent, AnotherFakeEvent>
        {
            public Task<AnotherFakeEvent> MapAsync(MappingData<AnotherFakeSourceEvent> mappingData)
            {
                throw new NotImplementedException();
            }
        }
    }
}
