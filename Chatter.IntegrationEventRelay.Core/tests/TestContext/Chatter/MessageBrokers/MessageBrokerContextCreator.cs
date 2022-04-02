using Chatter.CQRS.Context;
using Chatter.MessageBrokers.Context;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.MessageBrokers;

public class MessageBrokerContextCreator : MockCreator<IMessageBrokerContext>
{
	private readonly Mock<IMessageBrokerContext> _mockMessageBrokersContext = new();

	public MessageBrokerContextCreator(IMockContext newContext, IMessageBrokerContext creation = null)
		: base(newContext, creation)
	{
		_mockMessageBrokersContext.SetupGet(m => m.Container).Returns(new ContextContainer());
		Mock = _mockMessageBrokersContext.Object;
	}
}
