using Chatter.CQRS.DependencyInjection;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;

public class ChatterBuilderCreator : MockCreator<IChatterBuilder>
{
    private readonly Mock<IChatterBuilder> _mockChatterBuilder = new();

    public ChatterBuilderCreator(IMockContext newContext, IChatterBuilder creation = null)
        : base(newContext, creation)
    {
        _mockChatterBuilder.SetupGet(c => c.Configuration).Returns(Context.Common().Configuration.Mock);
        _mockChatterBuilder.SetupGet(c => c.AssemblySourceFilter).Returns(Context.Cqrs().AssemblySourceFilter.Mock);
        _mockChatterBuilder.SetupGet(c => c.Services).Returns(new ServiceCollection());
        Mock = _mockChatterBuilder.Object;
    }

    public ChatterBuilderCreator WithAssemblySourceFilter(IAssemblySourceFilter? assemblySourceFilter)
    {
        _mockChatterBuilder.SetupGet(c => c.AssemblySourceFilter).Returns(assemblySourceFilter);
        return this;
    }

    public ChatterBuilderCreator WithServiceCollection(IServiceCollection services)
    {
        _mockChatterBuilder.SetupGet(c => c.Services).Returns(services);
        return this;
    }

    public ChatterBuilderCreator WithConfiguration(IConfiguration configuration)
    {
        _mockChatterBuilder.SetupGet(c => c.Configuration).Returns(configuration);
        return this;
    }
}
