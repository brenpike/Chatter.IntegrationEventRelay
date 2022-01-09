using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Configuration.UsingEventMappingConfigItemProvider
{
    public class WhenInitializing : MockContext
    {
        [Fact]
        public void MustThrowIfLoggerIsNull()
        {
            var config = Context.Configuration().EventMappingConfiguration.Mock;
            Assert.Throws<ArgumentNullException>(() => new EventMappingConfigItemProvider(config, null));
        }

        [Fact]
        public void MustThrowIfConfigIsNull()
        {
            var logger = Context.Common().Logger<EventMappingConfigItemProvider>().Mock;
            Assert.Throws<ArgumentNullException>(() => new EventMappingConfigItemProvider(null, logger));
        }
    }
}
