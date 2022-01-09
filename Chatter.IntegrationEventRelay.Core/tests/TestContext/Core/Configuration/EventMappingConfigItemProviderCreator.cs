using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.SqlTableWatcher;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;

public class EventMappingConfigItemProviderCreator : MockCreator<IEventMappingConfigItemProvider>
{
    private readonly Mock<IEventMappingConfigItemProvider> _mockEventMappingConfigProvider = new();

    public EventMappingConfigItemProviderCreator(IMockContext newContext, IEventMappingConfigItemProvider creation = null)
        : base(newContext, creation)
    {
        _mockEventMappingConfigProvider.Setup(e => e.Get<ISourceEvent, IEvent>(It.IsAny<ChangeTypes>())).Returns(Context.Configuration().EventMappingConfigurationItem.Mock);
        Mock = _mockEventMappingConfigProvider.Object;
    }

    public EventMappingConfigItemProviderCreator VerifyConfigurationWasProvided(Times times)
    {
        _mockEventMappingConfigProvider.Verify(g => g.Get<ISourceEvent, IEvent>(It.IsAny<ChangeTypes>()), times);
        return this;
    }

    public EventMappingConfigItemProviderCreator VerifyConfigurationWasProvided<TSourceEvent, TIntegrationEvent>(Times times)
        where TSourceEvent : class, ISourceEvent
        where TIntegrationEvent : class, IEvent
    {
        _mockEventMappingConfigProvider.Verify(g => g.Get<TSourceEvent, TIntegrationEvent>(It.IsAny<ChangeTypes>()), times);
        return this;
    }

    public EventMappingConfigItemProviderCreator SetupGet<TSourceEvent, TIntegrationEvent>(ChangeTypes changeType, EventMappingConfigurationItem itemToProvide)
        where TSourceEvent : class, ISourceEvent
        where TIntegrationEvent : class, IEvent
    {
        _mockEventMappingConfigProvider.Setup(e => e.Get<TSourceEvent, TIntegrationEvent>(changeType)).Returns(itemToProvide);
        return this;
    }
}
