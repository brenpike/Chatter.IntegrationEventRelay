using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;

public class MapSourceInsertToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> : MockCreator<IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly Mock<IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent>> _mockMapper = new();

    public MapSourceInsertToIntegrationEventCreator(IMockContext newContext, IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent> creation = null)
        : base(newContext, creation)
    {
        Mock = _mockMapper.Object;
    }
}
