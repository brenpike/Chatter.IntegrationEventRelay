namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;

public static class CqrsExtensions
{
    public static CqrsMockContext Cqrs(this IMockContext context) => new(context);
    public class CqrsMockContext
    {
        private IMockContext TestContext { get; }
        public CqrsMockContext(IMockContext context) => TestContext = context;
        public MessageHandlerContextCreator MessageHandlerContext => new(TestContext);
        public ChatterBuilderCreator ChatterBuilder => new(TestContext);
        public AssemblySourceFilterCreator AssemblySourceFilter => new(TestContext);
    }
}
