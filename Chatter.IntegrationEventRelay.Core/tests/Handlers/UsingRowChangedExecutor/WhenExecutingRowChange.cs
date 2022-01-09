using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Handlers.UsingRowChangedExecutor
{
    public class WhenExecutingRowChange : MockContext
    {
        private readonly MessageHandlerContextCreator _mhc;
        private readonly MappingData<FakeSourceEvent> _mappingData;
        private readonly RowChangeExecutor<FakeSourceEvent, FakeEvent> _sut;
        private readonly LoggerCreator<RowChangeExecutor<FakeSourceEvent, FakeEvent>> _logger;
        private readonly RelayIntegrationEventCreator _relayIntegrationEvent;
        private readonly MapSourceToIntegrationEventCreator<FakeSourceEvent, FakeEvent> _mapper;
        private readonly EventMappingConfigurationItemCreator _item;

        public WhenExecutingRowChange()
        {
            _logger = Context.Common().Logger<RowChangeExecutor<FakeSourceEvent, FakeEvent>>();
            _relayIntegrationEvent = Context.Core().RelayIntegrationEvent;
            _mapper = Context.Mapper().MapSourceToIntegrationEvent<FakeSourceEvent, FakeEvent>();
            _item = Context.Configuration().EventMappingConfigurationItem;
            _mhc = Context.Cqrs().MessageHandlerContext;
            _mappingData = new MappingData<FakeSourceEvent>(new FakeSourceEvent());
            _sut = new RowChangeExecutor<FakeSourceEvent, FakeEvent>(_logger.Mock, _relayIntegrationEvent.Mock);
        }

        [Fact]
        public async Task MustThrowIfMappingDataIsNull()
            => await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.Execute(_mapper.Mock, null, _item.Mock, _mhc.Mock));

        [Fact]
        public async Task MustThrowIfSourceToIntegrationEventMapperIsNull()
            => await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.Execute(null, _mappingData, _item.Mock, _mhc.Mock));

        [Fact]
        public async Task MustThrowIfEventMappingConfigItemIsNull()
            => await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.Execute(_mapper.Mock, _mappingData, null, _mhc.Mock));

        [Fact]
        public async Task MustThrowIfMessageHandlerContextIsNull()
          => await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.Execute(_mapper.Mock, _mappingData, _item.Mock, null));

        [Fact]
        public async Task MustMapSourceToIntegrationEvent()
        {
            await _sut.Execute(_mapper.Mock, _mappingData, _item.Mock, _mhc.Mock);
            _mapper.VerifyMap(Times.Once());
        }

        [Fact]
        public async Task MustRelayIntegrationEventIfMapSourceToIntegrationEventIsSuccess()
        {
            await _sut.Execute(_mapper.Mock, _mappingData, _item.Mock, _mhc.Mock);
            _mapper.VerifyMap(Times.Once());
            _relayIntegrationEvent.VerifyRelay<FakeEvent>(Times.Once());
        }

        [Fact]
        public async Task MustLogErrorIfMapSourceToIntegrationEventFails()
        {
            _mapper.MapThrows();
            await Assert.ThrowsAnyAsync<Exception>(() => _sut.Execute(_mapper.Mock, _mappingData, _item.Mock, _mhc.Mock));
            _logger.VerifyWasCalled(LogLevel.Error, times: Times.Exactly(2));
        }

        [Fact]
        public async Task MustLogErrorIfRelayIntegrationEventFails()
        {
            _relayIntegrationEvent.RelayThrows();
            await Assert.ThrowsAnyAsync<Exception>(() => _sut.Execute(_mapper.Mock, _mappingData, _item.Mock, _mhc.Mock));
            _logger.VerifyWasCalled(LogLevel.Error, times: Times.Once());
        }

        public class FakeEvent : IEvent { }
        public class FakeSourceEvent : ISourceEvent { }
    }
}
