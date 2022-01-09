using Chatter.CQRS;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.SqlTableWatcher;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Extensions.UsingChatterBuilderExtensions
{
    public class WhenWiringRowChangeEventHandlerFromConfig : MockContext
    {
        [Fact]
        public void MustThrowWhenSourceEventTypeIsNull()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem
                .WithIntegrationEventType(typeof(ISourceEvent))
                .Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<,>), config));
        }

        [Fact]
        public void MustThrowIfMapIsNull()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            Assert.Throws<ArgumentNullException>(() => builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<,>), null));
        }

        [Fact]
        public void MustThrowIfRowChangedEventTypeIsNull()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem.Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireRowChangeEventHandlerFromConfig(null, typeof(RowUpdatedEventHandler<,>), config));
        }

        [Fact]
        public void MustThrowIfRowChangedEventHandlerTypeIsNull()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem.Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<ISourceEvent>), null, config));
        }

        [Fact]
        public void MustThrowIfClosedGenericRowChangedEventTypeIsSupplied()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent))
                .Mock;

            Assert.Throws<ArgumentException>(() => builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<ISourceEvent>), typeof(RowUpdatedEventHandler<,>), config));
        }

        [Fact]
        public void MustThrowIfClosedGenericRowChangedHandlerEventTypeIsSupplied()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent))
                .Mock;

            Assert.Throws<ArgumentException>(() => builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<ISourceEvent, ISourceEvent>), config));
        }

        [Fact]
        public void MustThrowWhenIntegrationEventTypeIsNull()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(ISourceEvent))
                .Mock;

            Assert.Throws<ArgumentNullException>(() => builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<,>), config));
        }

        [Fact]
        public void MustWireRowChangedEventHandlerForSuppliedTypes()
        {
            var builder = Context.Cqrs().ChatterBuilder.Mock;
            var config = Context.Configuration().EventMappingConfigurationItem
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent))
                .Mock;

            builder.WireRowChangeEventHandlerFromConfig(typeof(RowUpdatedEvent<>), typeof(RowUpdatedEventHandler<,>), config);

            Assert.Equal(1, builder.Services.Count);
            Assert.All(builder.Services, sd => Assert.Equal(typeof(RowUpdatedEventHandler<ISourceEvent, ISourceEvent>), sd?.ImplementationType));
            Assert.All(builder.Services, sd => Assert.Equal(typeof(IMessageHandler<RowUpdatedEvent<ISourceEvent>>), sd?.ServiceType));
        }
    }
}
