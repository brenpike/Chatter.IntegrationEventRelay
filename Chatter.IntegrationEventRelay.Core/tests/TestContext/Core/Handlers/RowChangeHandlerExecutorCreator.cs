using Chatter.CQRS.Context;
using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Handlers;
using Chatter.IntegrationEventRelay.Core.Mapping;
using Moq;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Handlers;

public class RowChangeHandlerExecutorCreator<TSourceEvent, TIntegrationEvent> : MockCreator<IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent>>
    where TSourceEvent : class, ISourceEvent
    where TIntegrationEvent : class, IEvent
{
    private readonly Mock<IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent>> _mockExecutor = new();

    public RowChangeHandlerExecutorCreator(IMockContext newContext, IRowChangeHandlerExecutor<TSourceEvent, TIntegrationEvent> creation = null)
        : base(newContext, creation)
    {
        _mockExecutor.Setup(e => e.Execute(It.IsAny<IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>>(), It.IsAny<MappingData<TSourceEvent>>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<IMessageHandlerContext>())).Returns(Task.CompletedTask);
        Mock = _mockExecutor.Object;
    }

    public RowChangeHandlerExecutorCreator<TSourceEvent, TIntegrationEvent> VerifyExecution(Times times)
    {
        _mockExecutor.Verify(e => e.Execute(It.IsAny<IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent>>(), It.IsAny<MappingData<TSourceEvent>>(), It.IsAny<EventMappingConfigurationItem>(), It.IsAny<IMessageHandlerContext>()), times);
        return this;
    }

    public RowChangeHandlerExecutorCreator<TSourceEvent, TIntegrationEvent> SetupExecute(IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent> mapper, MappingData<TSourceEvent> mappingData, EventMappingConfigurationItem mappingConfig, IMessageHandlerContext context)
    {
        _mockExecutor.Setup(e => e.Execute(mapper, mappingData, mappingConfig, context));
        return this;
    }
}
