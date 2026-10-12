using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Publishing;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.MessageBrokers;
using Chatter.MessageBrokers.Context;
using Chatter.MessageBrokers.Routing.Options;
using Chatter.MessageBrokers.Sending;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Publishing.UsingBrokeredIntegrationEventPublisher
{
    public class WhenPublishing : MockContext
    {
        private readonly Mock<IBrokeredMessageDispatcher> _dispatcher = new();
        private readonly BrokeredIntegrationEventPublisher _sut;
        private readonly EventMappingConfigurationItem _mapping;
        private readonly FakeEvent _event = new();
        private string _destination;
        private TransactionContext _transactionContext = new TransactionContext("not-captured", Chatter.MessageBrokers.Receiving.TransactionMode.ReceiveOnly);
        private PublishOptions _options;

        public WhenPublishing()
        {
            _dispatcher.Setup(d => d.Publish(It.IsAny<FakeEvent>(), It.IsAny<string>(), It.IsAny<TransactionContext>(), It.IsAny<PublishOptions>()))
                .Callback<FakeEvent, string, TransactionContext, PublishOptions>((_, destination, transactionContext, options) =>
                {
                    _destination = destination;
                    _transactionContext = transactionContext;
                    _options = options;
                })
                .Returns(Task.CompletedTask);

            _mapping = Context.Configuration().EventMappingConfigurationItem.Mock;
            _mapping.InfrastructureMessageName = "event-topic";
            _mapping.MessagingInfrastructureType = "Chatter.Infrastructure.AzureServiceBus";
            _mapping.BrokeredMessageContentType = "application/json";

            _sut = new BrokeredIntegrationEventPublisher(_dispatcher.Object, NullLogger<BrokeredIntegrationEventPublisher>.Instance);
        }

        [Fact]
        public async Task MustPublishTheEventOnce()
        {
            await _sut.PublishAsync(_event, _mapping, null, CancellationToken.None);

            _dispatcher.Verify(d => d.Publish(_event, It.IsAny<string>(), It.IsAny<TransactionContext>(), It.IsAny<PublishOptions>()), Times.Once());
        }

        [Fact]
        public async Task MustPublishToTheMappingsInfrastructureMessageName()
        {
            await _sut.PublishAsync(_event, _mapping, null, CancellationToken.None);

            Assert.Equal("event-topic", _destination);
        }

        [Fact]
        public async Task MustPublishOutsideOfAnyInboundTransaction()
        {
            await _sut.PublishAsync(_event, _mapping, null, CancellationToken.None);

            Assert.Null(_transactionContext);
        }

        [Fact]
        public async Task MustUseTheMappingsContentType()
        {
            _mapping.BrokeredMessageContentType = "application/xml";

            await _sut.PublishAsync(_event, _mapping, null, CancellationToken.None);

            Assert.Equal("application/xml", _options.ContentType);
        }

        [Fact]
        public async Task MustUseTheMappingsMessagingInfrastructure()
        {
            await _sut.PublishAsync(_event, _mapping, null, CancellationToken.None);

            // RoutingOptions.MessageContext is internal to Chatter
            var messageContext = (IDictionary<string, object>)typeof(RoutingOptions)
                .GetProperty("MessageContext", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_options);
            Assert.Equal("Chatter.Infrastructure.AzureServiceBus", messageContext[MessageContext.InfrastructureType]);
        }

        [Fact]
        public async Task MustUseTheSuppliedMessageId()
        {
            await _sut.PublishAsync(_event, _mapping, "change-position:FakeEvent", CancellationToken.None);

            Assert.Equal("change-position:FakeEvent", _options.MessageId);
        }

        [Fact]
        public async Task MustLeaveTheMessageIdUnsetWhenNoneIsSupplied()
        {
            await _sut.PublishAsync(_event, _mapping, null, CancellationToken.None);

            Assert.Null(_options.MessageId);
        }

        [Fact]
        public async Task MustPropagatePublishFailures()
        {
            _dispatcher.Setup(d => d.Publish(It.IsAny<FakeEvent>(), It.IsAny<string>(), It.IsAny<TransactionContext>(), It.IsAny<PublishOptions>()))
                .ThrowsAsync(new InvalidOperationException("broker unavailable"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.PublishAsync(_event, _mapping, null, CancellationToken.None));
        }

        [Fact]
        public async Task MustThrowWhenTheEventIsNull()
            => await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.PublishAsync<FakeEvent>(null, _mapping, null, CancellationToken.None));

        [Fact]
        public async Task MustThrowWhenTheMappingIsNull()
            => await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.PublishAsync(_event, null, null, CancellationToken.None));

        public class FakeEvent : IEvent { }
    }
}
