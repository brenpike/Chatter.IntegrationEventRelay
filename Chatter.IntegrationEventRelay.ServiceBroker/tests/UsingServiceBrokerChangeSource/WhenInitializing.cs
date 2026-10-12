using Chatter.IntegrationEventRelay.Core.Abstractions;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Tests.UsingServiceBrokerChangeSource
{
    public class WhenInitializing
    {
        [Fact]
        public void MustExposeItsName()
            => Assert.Equal("fakedb-ssb", new ServiceBrokerChangeSource("fakedb-ssb").Name);

        [Fact]
        public void MustBeOfTypeServiceBroker()
            => Assert.Equal("ServiceBroker", new ServiceBrokerChangeSource("fakedb-ssb").Type);

        [Fact]
        public void MustNotClaimOrderingResumabilityOrCommitTimes()
            => Assert.Equal(ChangeSourceCapabilities.None, new ServiceBrokerChangeSource("fakedb-ssb").Capabilities);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void MustThrowWithoutAName(string? name)
            => Assert.Throws<ArgumentNullException>(() => new ServiceBrokerChangeSource(name!));
    }
}
