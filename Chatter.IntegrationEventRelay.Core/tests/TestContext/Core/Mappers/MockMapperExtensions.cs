using Chatter.CQRS.Events;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;

public static class MockMapperExtensions
{
    public static MockMapperContext Mapper(this IMockContext context) => new(context);
    public class MockMapperContext
    {
        private IMockContext TestContext { get; }
        public MockMapperContext(IMockContext context) => TestContext = context;
        public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>()
            where TSourceEvent : class, ISourceEvent
            where TIntegrationEvent : class, IEvent
            => new(TestContext);
        public MapSourceInsertToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent>()
            where TSourceEvent : class, ISourceEvent
            where TIntegrationEvent : class, IEvent
            => new(TestContext);
        public MapSourceUpdateToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent>()
            where TSourceEvent : class, ISourceEvent
            where TIntegrationEvent : class, IEvent
            => new(TestContext);
        public MapSourceDeleteToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent>()
            where TSourceEvent : class, ISourceEvent
            where TIntegrationEvent : class, IEvent
            => new(TestContext);
    }
}
