using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core;
using Microsoft.Extensions.Logging;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Handlers.UsingRowChangedExecutor
{
    public class WhenInitializing : MockContext
    {
        private readonly ILogger<RowChangeExecutor<ISourceEvent, IEvent>> _logger;
        private readonly IRelayIntegrationEvent _relayIntegrationEvent;

        public WhenInitializing()
        {
            _logger = Context.Common().Logger<RowChangeExecutor<ISourceEvent, IEvent>>().Mock;
            _relayIntegrationEvent = Context.Core().RelayIntegrationEvent.Mock;
        }

        [Fact]
        public void MustThrowWhenLoggerIsNull()
            => Assert.Throws<ArgumentNullException>(() => new RowChangeExecutor<ISourceEvent, IEvent>(null, _relayIntegrationEvent));

        [Fact]
        public void MustThrowWhenRelayIntegrationEventIsNull()
            => Assert.Throws<ArgumentNullException>(() => new RowChangeExecutor<ISourceEvent, IEvent>(_logger, null));
    }
}
