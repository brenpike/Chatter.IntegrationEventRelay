using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.MessageBrokers.Context;
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
        _mockRelay.Setup(m => m.RelayAsync(It.IsAny<IEvent>(), It.IsAny<IMessageBrokerContext>(), It.IsAny<EventMappingConfigurationItem>())).Returns(Task.CompletedTask);
        Mock = _mockRelay.Object;
    }

    public RelayIntegrationEventCreator SetupRelay<TIntegrationEvent>(TIntegrationEvent @event, IMessageBrokerContext context, EventMappingConfigurationItem mapping)
        where TIntegrationEvent : class, IEvent
    {
        _mockRelay.Setup(m => m.RelayAsync(@event, context, mapping)).Returns(Task.CompletedTask);
        return this;
    }

    public RelayIntegrationEventCreator VerifyRelay<TIntegrationEvent>(Times times)
        where TIntegrationEvent : class, IEvent
    {
        _mockRelay.Verify(r => r.RelayAsync(It.IsAny<TIntegrationEvent>(), It.IsAny<IMessageBrokerContext>(), It.IsAny<EventMappingConfigurationItem>()));
        return this;
    }

    public RelayIntegrationEventCreator RelayThrows<TException>(TException exception)
        where TException : Exception, new()
    {
        _mockRelay.Setup(r => r.RelayAsync(It.IsAny<IEvent>(), It.IsAny<IMessageBrokerContext>(), It.IsAny<EventMappingConfigurationItem>())).ThrowsAsync(exception);
        return this;
    }

    public RelayIntegrationEventCreator RelayThrows()
        => RelayThrows(new Exception());
}
