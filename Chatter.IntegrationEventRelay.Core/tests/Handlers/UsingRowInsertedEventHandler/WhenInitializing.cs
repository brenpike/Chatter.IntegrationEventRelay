using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;
using Microsoft.Extensions.Logging;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Handlers.UsingRowInsertedEventHandler
{
    public class WhenInitializing : MockContext
    {
        private readonly ILogger<RowInsertedEventHandler<ISourceEvent, IEvent>> _logger;
        private readonly IMapSourceInsertToIntegrationEvent<ISourceEvent, IEvent> _mapper;
        private readonly IEventMappingConfigItemProvider _configItemProvider;
        private readonly IRowChangeHandlerExecutor<ISourceEvent, IEvent> _rowChangeExecutor;

        public WhenInitializing()
        {
            _logger = Context.Common().Logger<RowInsertedEventHandler<ISourceEvent, IEvent>>().Mock;
            _mapper = Context.Mapper().MapSourceInsertToIntegrationEvent<ISourceEvent, IEvent>().Mock;
            _configItemProvider = Context.Configuration().EventMappingConfigItemProvider.Mock;
            _rowChangeExecutor = Context.Handler().RowChangeHandlerExecutor<ISourceEvent, IEvent>().Mock;
        }

        [Fact]
        public void MustThrowWhenLoggerIsNull()
            => Assert.Throws<ArgumentNullException>(() => new RowInsertedEventHandler<ISourceEvent, IEvent>(null, _mapper, _configItemProvider, _rowChangeExecutor));

        [Fact]
        public void MustThrowWhenIntegrationEventMapperIsNull()
            => Assert.Throws<ArgumentNullException>(() => new RowInsertedEventHandler<ISourceEvent, IEvent>(_logger, null, _configItemProvider, _rowChangeExecutor));

        [Fact]
        public void MustThrowWhenEventMappingConfigurationItemProviderIsNull()
            => Assert.Throws<ArgumentNullException>(() => new RowInsertedEventHandler<ISourceEvent, IEvent>(_logger, _mapper, null, _rowChangeExecutor));

        [Fact]
        public void MustThrowWhenRowChangeHandlerExecutorIsNull()
            => Assert.Throws<ArgumentNullException>(() => new RowInsertedEventHandler<ISourceEvent, IEvent>(_logger, _mapper, _configItemProvider, null));
    }
}
