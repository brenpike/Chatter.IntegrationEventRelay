using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Moq;
using System;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core;

public class RelayIntegrationEventCreator : MockCreator<IRelayIntegrationEvent>
{
    private readonly Mock<IRelayIntegrationEvent> _mockRelay = new();

    public RelayIntegrationEventCreator(IMockContext newContext, IRelayIntegrationEvent creation = null)
        : base(newContext, creation)
    {
        _mockRelay.Setup(m => m.Relay(It.IsAny<IEvent>(), It.IsAny<IMessageHandlerContext>(), It.IsAny<EventMappingConfigurationItem>())).Returns(Task.CompletedTask);
        Mock = _mockRelay.Object;
    }

    public RelayIntegrationEventCreator SetupRelay<TIntegrationEvent>(TIntegrationEvent @event, IMessageHandlerContext context, EventMappingConfigurationItem mapping)
        where TIntegrationEvent : class, IEvent
    {
        _mockRelay.Setup(m => m.Relay(@event, context, mapping)).Returns(Task.CompletedTask);
        return this;
    }

    public RelayIntegrationEventCreator VerifyRelay<TIntegrationEvent>(Times times)
        where TIntegrationEvent : class, IEvent
    {
        _mockRelay.Verify(r => r.Relay(It.IsAny<TIntegrationEvent>(), It.IsAny<IMessageHandlerContext>(), It.IsAny<EventMappingConfigurationItem>()));
        return this;
    }

    public RelayIntegrationEventCreator RelayThrows<TException>(TException exception)
        where TException : Exception, new()
    {
        _mockRelay.Setup(r => r.Relay(It.IsAny<IEvent>(), It.IsAny<IMessageHandlerContext>(), It.IsAny<EventMappingConfigurationItem>())).ThrowsAsync(exception);
        return this;
    }

    public RelayIntegrationEventCreator RelayThrows()
        => RelayThrows(new Exception());
}
