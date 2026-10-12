using Chatter.IntegrationEventRelay.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Validation.UsingStartupValidationService
{
    public class WhenStarting
    {
        [Fact]
        public async Task MustStartWhenEveryCheckPasses()
        {
            var sut = CreateSut(Check(), Check());

            await sut.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task MustFailWithEveryProblemFromEveryCheck()
        {
            var sut = CreateSut(Check("first problem"), Check("second problem", "third problem"));

            var e = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(CancellationToken.None));

            Assert.Contains("first problem", e.Message);
            Assert.Contains("second problem", e.Message);
            Assert.Contains("third problem", e.Message);
        }

        private static StartupValidationService CreateSut(params IRelayStartupCheck[] checks)
        {
            var services = new ServiceCollection();
            foreach (var check in checks)
                services.AddSingleton(check);
            return new StartupValidationService(services.BuildServiceProvider(), NullLogger<StartupValidationService>.Instance);
        }

        private static IRelayStartupCheck Check(params string[] problems)
        {
            var check = new Mock<IRelayStartupCheck>();
            check.Setup(c => c.Validate(It.IsAny<IServiceProvider>())).Returns(problems);
            return check.Object;
        }
    }
}
