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

namespace Chatter.IntegrationEventRelay.Core.Tests.Handlers.UsingRowDeletedEventHandler
{
    public class WhenHandlingRowDeletedEvent : MockContext
    {
        private readonly RowDeletedEventHandler<ISourceEvent, IEvent> _sut;
        private readonly LoggerCreator<RowDeletedEventHandler<ISourceEvent, IEvent>> _logger;
        private readonly MapSourceDeleteToIntegrationEventCreator<ISourceEvent, IEvent> _mapper;
        private readonly EventMappingConfigItemProviderCreator _configItemProvider;
        private readonly RowChangeHandlerExecutorCreator<ISourceEvent, IEvent> _rowChangeExecutor;
        private readonly RowDeletedEvent<ISourceEvent> _rowDeletedEvent;

        public WhenHandlingRowDeletedEvent()
        {
            _logger = Context.Common().Logger<RowDeletedEventHandler<ISourceEvent, IEvent>>();
            _mapper = Context.Mapper().MapSourceDeleteToIntegrationEvent<ISourceEvent, IEvent>();
            _configItemProvider = Context.Configuration().EventMappingConfigItemProvider;
            _rowChangeExecutor = Context.Handler().RowChangeHandlerExecutor<ISourceEvent, IEvent>();
            _rowDeletedEvent = new RowDeletedEvent<ISourceEvent>(new FakeSourceEvent());
            _sut = new(_logger.Mock, _mapper.Mock, _configItemProvider.Mock, _rowChangeExecutor.Mock);
        }

        [Fact]
        public void MustLogInformation()
        {
            _sut.Handle(_rowDeletedEvent, It.IsAny<IMessageHandlerContext>());
            _logger.VerifyWasCalled(LogLevel.Information, times: Times.Once());
        }

        [Fact]
        public void MustGetEventMappingConfigItem()
        {
            _sut.Handle(_rowDeletedEvent, It.IsAny<IMessageHandlerContext>());
            _configItemProvider.VerifyConfigurationWasProvided(Times.Once());
        }

        [Fact]
        public void MustExecuteOnRowChange()
        {
            _sut.Handle(_rowDeletedEvent, It.IsAny<IMessageHandlerContext>());
            _rowChangeExecutor.VerifyExecution(Times.Once());
        }

        private class FakeSourceEvent : ISourceEvent { }
    }
}
