namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;

public static class CommonExtensions
{
    public static CommonMockContext Common(this IMockContext context) => new(context);
    public class CommonMockContext
    {
        private IMockContext TestContext { get; }
        public CommonMockContext(IMockContext context) => TestContext = context;
        public LoggerCreator<T> Logger<T>() => new(TestContext);
        public AssemblyCreator Assembly => new(TestContext);
        public TypeCreator Type => new(TestContext);
        public ConfigurationCreator Configuration => new(TestContext);
    }
}
