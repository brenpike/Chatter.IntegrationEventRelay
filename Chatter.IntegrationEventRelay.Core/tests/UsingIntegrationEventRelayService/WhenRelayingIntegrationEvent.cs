using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.MessageBrokers.Context;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.UsingIntegrationEventRelayService;

public class WhenRelayingIntegrationEvent : MockContext
{
    private readonly LoggerCreator<IntegrationEventRelayService> _logger;
    private readonly IntegrationEventRelayService _sut;

    public WhenRelayingIntegrationEvent()
    {
        _logger = Context.Common().Logger<IntegrationEventRelayService>();
        _sut = new IntegrationEventRelayService(_logger.Mock);
    }

    [Fact]
    public async Task MustLogWarningIfNullEventMappingConfigSupplied()
    {
        await _sut.RelayAsync(It.IsAny<IEvent>(), It.IsAny<IMessageBrokerContext>(), null);
        _logger.VerifyWasCalled(LogLevel.Warning, times: Times.Once());
    }

    [Fact]
    public async Task MustNotLogSuccessMessageIfNullEventMappingConfigSupplied()
    {
        await _sut.RelayAsync(It.IsAny<IEvent>(), It.IsAny<IMessageBrokerContext>(), null);
        _logger.VerifyWasCalled(LogLevel.Information, "Successfully published integration event to messaging infrastructure", Times.Never());
    }

    [Fact]
    public async Task MustLogInformationIfIntegrationEventIsNull()
    {
        await _sut.RelayAsync<IEvent>(null, It.IsAny<IMessageBrokerContext>(), Context.Configuration().EventMappingConfigurationItem.Mock);
        var str = $"Integration event '{typeof(IEvent).Name}' is null. Skipping publish to messaging infrastructure.";
        _logger.VerifyWasCalled(LogLevel.Information, str, Times.Once());
    }

    [Fact]
    public async Task MustNotLogSuccessMessageIfIntegrationEventIsNull()
    {
        await _sut.RelayAsync<IEvent>(null, It.IsAny<IMessageBrokerContext>(), Context.Configuration().EventMappingConfigurationItem.Mock);
        _logger.VerifyWasCalled(LogLevel.Information, "Successfully published integration event to messaging infrastructure", Times.Never());
    }

    [Fact]
    public async Task MustLogErrorIfThrowsException()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.RelayAsync(new FakeEvent(), null, Context.Configuration().EventMappingConfigurationItem.Mock));
        _logger.VerifyWasCalled(LogLevel.Error, times: Times.Once());
    }

    private record FakeEvent : IEvent { }
}
