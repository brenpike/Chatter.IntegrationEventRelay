namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core;

public static class MockCoreExtensions
{
    public static MockCoreContext Core(this IMockContext context) => new(context);
    public class MockCoreContext
    {
        private IMockContext TestContext { get; }
        public MockCoreContext(IMockContext context) => TestContext = context;
        public RelayIntegrationEventCreator RelayIntegrationEvent => new(TestContext);
    }
}
