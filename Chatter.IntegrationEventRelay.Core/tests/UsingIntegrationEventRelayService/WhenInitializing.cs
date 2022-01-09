using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.UsingIntegrationEventRelayService;

public class WhenInitializing : MockContext
{
    [Fact]
    public void MustThrowIfLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new IntegrationEventRelayService(null));
    }
}
