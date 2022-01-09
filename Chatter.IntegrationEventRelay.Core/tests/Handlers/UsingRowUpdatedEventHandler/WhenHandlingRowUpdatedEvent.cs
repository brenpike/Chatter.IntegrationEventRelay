using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;
using Chatter.SqlTableWatcher;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Handlers.UsingRowUpdatedEventHandler
{
    public class WhenHandlingRowUpdatedEvent : MockContext
    {
        private readonly RowUpdatedEventHandler<ISourceEvent, IEvent> _sut;
        private readonly LoggerCreator<RowUpdatedEventHandler<ISourceEvent, IEvent>> _logger;
        private readonly MapSourceUpdateToIntegrationEventCreator<ISourceEvent, IEvent> _mapper;
        private readonly EventMappingConfigItemProviderCreator _configItemProvider;
        private readonly RowChangeHandlerExecutorCreator<ISourceEvent, IEvent> _rowChangeExecutor;
        private readonly RowUpdatedEvent<ISourceEvent> _rowUpdatedEvent;

        public WhenHandlingRowUpdatedEvent()
        {
            _logger = Context.Common().Logger<RowUpdatedEventHandler<ISourceEvent, IEvent>>();
            _mapper = Context.Mapper().MapSourceUpdateToIntegrationEvent<ISourceEvent, IEvent>();
            _configItemProvider = Context.Configuration().EventMappingConfigItemProvider;
            _rowChangeExecutor = Context.Handler().RowChangeHandlerExecutor<ISourceEvent, IEvent>();
            _rowUpdatedEvent = new RowUpdatedEvent<ISourceEvent>(new FakeSourceEvent(), new FakeSourceEvent());
            _sut = new(_logger.Mock, _mapper.Mock, _configItemProvider.Mock, _rowChangeExecutor.Mock);
        }

        [Fact]
        public void MustLogInformation()
        {
            _sut.Handle(_rowUpdatedEvent, It.IsAny<IMessageHandlerContext>());
            _logger.VerifyWasCalled(LogLevel.Information, times: Times.Once());
        }

        [Fact]
        public void MustGetEventMappingConfigItem()
        {
            _sut.Handle(_rowUpdatedEvent, It.IsAny<IMessageHandlerContext>());
            _configItemProvider.VerifyConfigurationWasProvided(Times.Once());
        }

        [Fact]
        public void MustExecuteOnRowChange()
        {
            _sut.Handle(_rowUpdatedEvent, It.IsAny<IMessageHandlerContext>());
            _rowChangeExecutor.VerifyExecution(Times.Once());
        }

        private class FakeSourceEvent : ISourceEvent { }
    }
}
