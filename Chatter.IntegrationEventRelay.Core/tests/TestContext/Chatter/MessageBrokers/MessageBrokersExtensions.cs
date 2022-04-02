namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.MessageBrokers;

public static class MessageBrokersExtensions
{
	public static MessageBrokersMockContext MessageBrokers(this IMockContext context) => new(context);
	public class MessageBrokersMockContext
	{
		private IMockContext TestContext { get; }
		public MessageBrokersMockContext(IMockContext context) => TestContext = context;
		public MessageBrokerContextCreator MessageBrokerContext => new(TestContext);
	}
}
