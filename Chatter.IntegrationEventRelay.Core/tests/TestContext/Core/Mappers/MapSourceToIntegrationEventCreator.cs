using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Chatter.MessageBrokers.Context;
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

	public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> SetupMapAsync(MappingData<TSourceEvent> mappingData, TIntegrationEvent returnValue = null)
	{
		if (returnValue == null)
			returnValue = new Mock<TIntegrationEvent>().Object;

		_mockMapper.Setup(m => m.MapAsync(mappingData, It.IsAny<IMessageBrokerContext>(), It.IsAny<EventMappingConfigurationItem>())).ReturnsAsync(returnValue);
		return this;
	}

	public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> VerifyMap(Times times)
	{
		_mockMapper.Verify(m => m.MapAsync(It.IsAny<MappingData<TSourceEvent>>(), It.IsAny<IMessageBrokerContext>(), It.IsAny<EventMappingConfigurationItem>()), times);
		return this;
	}

	public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapThrows<TException>(TException exception)
		where TException : Exception, new()
	{
		_mockMapper.Setup(m => m.MapAsync(It.IsAny<MappingData<TSourceEvent>>(), It.IsAny<IMessageBrokerContext>(), It.IsAny<EventMappingConfigurationItem>())).ThrowsAsync(exception);
		return this;
	}

	public MapSourceToIntegrationEventCreator<TSourceEvent, TIntegrationEvent> MapThrows()
		=> MapThrows(new Exception());
}
