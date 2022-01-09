using Moq;
using System;
using System.Linq;
using System.Reflection;

namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;

public class AssemblyCreator : MockCreator<Assembly>
{
    private readonly Mock<Assembly> _assemblyMock = new Mock<Assembly>();

    public AssemblyCreator(IMockContext newContext, Assembly assembly = null)
        : base(newContext, assembly)
    {
        WithFullName(Guid.NewGuid().ToString());
        WithTypes(Context.Common().Type.Mock);
        _assemblyMock.Setup(a => a.Equals(It.IsAny<Assembly>()))
                     .Returns<Assembly>(x => x.FullName == _assemblyMock.Object.FullName
                                             && x.GetTypes() == _assemblyMock.Object.GetTypes());

        Mock = _assemblyMock.Object;
    }

    public AssemblyCreator WithTypes(params Type[] types)
    {
        _assemblyMock.Setup(a => a.GetTypes()).Returns(types);
        _assemblyMock.SetupGet(a => a.ExportedTypes).Returns(types);
        _assemblyMock.SetupGet(a => a.DefinedTypes).Returns(types.Cast<TypeInfo>());
        return this;
    }

    public AssemblyCreator WithFullName(string fullName)
    {
        _assemblyMock.SetupGet(a => a.FullName).Returns(fullName);
        return this;
    }
}
