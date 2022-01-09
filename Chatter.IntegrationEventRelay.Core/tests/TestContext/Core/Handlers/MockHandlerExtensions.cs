using Chatter.CQRS.Events;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Handlers;

public static class MockHandlerExtensions
{
    public static MockHandlerContext Handler(this IMockContext context) => new(context);
    public class MockHandlerContext
    {
        private IMockContext TestContext { get; }
        public MockHandlerContext(IMockContext context) => TestContext = context;
        public RowChangeHandlerExecutorCreator<TSourceEvent, TIntegrationEvent> RowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent>()
            where TSourceEvent : class, ISourceEvent
            where TIntegrationEvent : class, IEvent
            => new(TestContext);
    }
}
