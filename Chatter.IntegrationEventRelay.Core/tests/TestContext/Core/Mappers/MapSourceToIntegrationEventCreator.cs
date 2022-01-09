using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Moq;
using System;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Mappers;

public class MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> : MockCreator<IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly Mock<IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>> _mockMapper = new();

    public MapSourceToIntegrationEventCreator(IMockContext newContext, IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent> creation = null)
        : base(newContext, creation)
    {
        Mock = _mockMapper.Object;
    }

    public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> SetupMapAsync(MappingData<TSourceEvent> mappingData, TIntegrationEvent? returnValue = null)
    {
        if (returnValue == null)
            returnValue = new Mock<TIntegrationEvent>().Object;

        _mockMapper.Setup(m => m.MapAsync(mappingData)).ReturnsAsync(returnValue);
        return this;
    }

    public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> VerifyMap(Times times)
    {
        _mockMapper.Verify(m => m.MapAsync(It.IsAny<MappingData<TSourceEvent>>()), times);
        return this;
    }

    public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapThrows<TException>(TException exception)
        where TException : Exception, new()
    {
        _mockMapper.Setup(m => m.MapAsync(It.IsAny<MappingData<TSourceEvent>>())).ThrowsAsync(exception);
        return this;
    }

    public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapThrows()
        => MapThrows(new Exception());
}
