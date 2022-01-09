using Chatter.CQRS.DependencyInjection;
using Moq;
using System.Reflection;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Chatter.Cqrs;

public class AssemblySourceFilterCreator : MockCreator<IAssemblySourceFilter>
{
    private readonly Mock<IAssemblySourceFilter> _mockAssemblySourceFilter = new();

    public AssemblySourceFilterCreator(IMockContext newContext, IAssemblySourceFilter creation = null)
        : base(newContext, creation)
    {
        Mock = _mockAssemblySourceFilter.Object;
    }

    public AssemblySourceFilterCreator WithAssemblySource(params Assembly[] assemblies)
    {
        _mockAssemblySourceFilter.Setup(a => a.Apply()).Returns(assemblies);
        return this;
    }
}
