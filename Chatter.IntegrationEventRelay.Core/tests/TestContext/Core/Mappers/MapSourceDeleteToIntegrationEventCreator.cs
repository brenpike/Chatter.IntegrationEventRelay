using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;

public class MapSourceDeleteToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> : MockCreator<IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly Mock<IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent>> _mockMapper = new();

    public MapSourceDeleteToIntegrationEventCreator(IMockContext newContext, IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent> creation = null)
        : base(newContext, creation)
    {
        Mock = _mockMapper.Object;
    }
}
