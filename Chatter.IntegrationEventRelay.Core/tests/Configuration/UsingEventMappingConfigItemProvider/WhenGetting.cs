using Chatter.CQRS.Events;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Common;
using Chatter.IntegrationEventRelay.Core.Tests.TestContext.Core.Configuration;
using Chatter.SqlChangeFeed;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Chatter.IntegrationEventRelay.Core.Tests.Configuration.UsingEventMappingConfigItemProvider
{
    public class WhenGetting : MockContext
    {
        private EventMappingConfigurationCreator _config;
        private readonly LoggerCreator<EventMappingConfigItemProvider> _logger;
        private readonly EventMappingConfigItemProvider _sut;

        public WhenGetting()
        {
            _config = Context.Configuration().EventMappingConfiguration;
            _logger = Context.Common().Logger<EventMappingConfigItemProvider>();
            _sut = new EventMappingConfigItemProvider(_config.Mock,
                                                  _logger.Mock);
        }

        [Fact]
        public void MustReturnNullIfNoMappingsWithMatchingChangeType()
        {
            var mapping1 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent));
            _config.WithMappings(mapping1);
            var result = _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Update);
            _logger.VerifyWasCalled(LogLevel.Debug, times: Times.Once());
            Assert.Null(result);
        }


        [Fact]
        public void MustReturnNullIfNoMappingsWithMatchingSourceType()
        {
            var mapping1 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithSourceEventType(typeof(IEvent))
                .WithIntegrationEventType(typeof(ISourceEvent));
            _config.WithMappings(mapping1);
            var result = _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            _logger.VerifyWasCalled(LogLevel.Debug, times: Times.Once());
            Assert.Null(result);
        }

        [Fact]
        public void MustReturnNullIfNoMappingsWithMatchingIntegrationType()
        {
            var mapping1 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(IEvent));
            _config.WithMappings(mapping1);
            var result = _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            _logger.VerifyWasCalled(LogLevel.Debug, times: Times.Once());
            Assert.Null(result);
        }

        [Fact]
        public void MustReturnEmptyMappingConfigurationIfNotMappingsFound()
        {
            var result = _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            _logger.VerifyWasCalled(LogLevel.Debug, times: Times.Once());
            Assert.Null(result);
        }

        [Fact]
        public void MustReturnMappingIfExactlyOneMatchingMappingFound()
        {
            var mapping1 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent));
            _config.WithMappings(mapping1);
            var result = _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            Assert.Equal(mapping1, result);
        }

        [Fact]
        public void MustLogWarningIfMoreThanOneMappingIsFound()
        {
            var mapping1 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent));
            _config.WithMappings(mapping1, mapping1);
            _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            _logger.VerifyWasCalled(LogLevel.Warning, times: Times.Once());
        }

        [Fact]
        public void MustReturnFirstMatchingMappingIfMoreThanOneMappingIsFound()
        {
            var expected = "first";
            var mapping1 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithDatabaseName(expected)
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent));
            var mapping2 = Context.Configuration().EventMappingConfigurationItem
                .WithChangeType(ChangeTypes.Insert)
                .WithDatabaseName("second")
                .WithSourceEventType(typeof(ISourceEvent))
                .WithIntegrationEventType(typeof(ISourceEvent));
            _config.WithMappings(mapping1, mapping2);
            var result = _sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            Assert.Equal(expected, result.DatabaseName);
        }

        [Fact]
        public void MustReturnNullMappingIfExceptionIsThrown()
        {
            _logger.WithLogDebugThatThrows();
            var sut = new EventMappingConfigItemProvider(_config.Mock, _logger.Mock);

            _config.WithMappings(null);
            var result = sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            Assert.Null(result);
        }

        [Fact]
        public void MustLogWarningIfExceptionIsThrown()
        {
            _logger.WithLogDebugThatThrows();
            var sut = new EventMappingConfigItemProvider(_config.Mock, _logger.Mock);

            _config.WithMappings(null);
            sut.Get<ISourceEvent, ISourceEvent>(ChangeTypes.Insert);
            _logger.VerifyWasCalled(LogLevel.Warning, times: Times.Once());
        }
    }
}
