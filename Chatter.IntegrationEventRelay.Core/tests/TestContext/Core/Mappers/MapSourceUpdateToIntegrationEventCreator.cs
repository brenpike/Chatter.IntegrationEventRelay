using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;

public class MapSourceUpdateToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> : MockCreator<IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly Mock<IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent>> _mockMapper = new();

    public MapSourceUpdateToIntegrationEventCreator(IMockContext newContext, IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent> creation = null)
        : base(newContext, creation)
    {
        Mock = _mockMapper.Object;
    }
}
