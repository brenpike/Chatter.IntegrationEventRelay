using Chatter.CQRS.Context;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;

public class MessageHandlerContextCreator : MockCreator<IMessageHandlerContext>
{
    private readonly Mock<IMessageHandlerContext> _mockMessageHandlerContext = new();

    public MessageHandlerContextCreator(IMockContext newContext, IMessageHandlerContext creation = null)
        : base(newContext, creation)
    {
        _mockMessageHandlerContext.SetupGet(m => m.Container).Returns(new ContextContainer());
        Mock = _mockMessageHandlerContext.Object;
    }
}
