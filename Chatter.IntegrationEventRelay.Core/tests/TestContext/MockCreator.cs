namespace Chatter.IntegrationEventRelay.Core.Tests.TestContext;

public class MockCreator<T>
{
    protected IMockContext Context { get; }
    public T Mock { get; protected set; }
    public MockCreator(IMockContext newContext, T creation = default)
    {
        Context = newContext;
        Mock = creation;
    }

    /// <summary>
    /// Implicitly converts the creator to instance of the creation
    /// </summary>
    /// <param name="creator"></param>
    public static implicit operator T(MockCreator<T> creator)
    {
        return creator.Mock;
    }
}
