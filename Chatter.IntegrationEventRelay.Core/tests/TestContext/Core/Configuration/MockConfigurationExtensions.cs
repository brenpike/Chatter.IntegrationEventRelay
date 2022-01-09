namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;

public static class MockConfigurationExtensions
{
    public static MockConfigurationContext Configuration(this IMockContext context) => new(context);
    public class MockConfigurationContext
    {
        private IMockContext TestContext { get; }
        public MockConfigurationContext(IMockContext context) => TestContext = context;
        public EventMappingConfigurationItemCreator EventMappingConfigurationItem => new(TestContext);
        public EventMappingConfigurationCreator EventMappingConfiguration => new(TestContext);
        public EventMappingConfigItemProviderCreator EventMappingConfigItemProvider => new(TestContext);
    }
}
