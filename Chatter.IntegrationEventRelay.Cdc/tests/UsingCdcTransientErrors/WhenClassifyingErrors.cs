using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcTransientErrors
{
    public class WhenClassifyingErrors
    {
        [Fact]
        public void MustTreatTimeoutsAndIoErrorsAsTransient()
        {
            Assert.True(CdcTransientErrors.IsTransient(new TimeoutException()));
            Assert.True(CdcTransientErrors.IsTransient(new IOException()));
            Assert.True(CdcTransientErrors.IsTransient(new TaskCanceledException()));
        }

        [Fact]
        public void MustTreatMappingErrorsAsPersistent()
        {
            Assert.False(CdcTransientErrors.IsTransient(new InvalidOperationException()));
            Assert.False(CdcTransientErrors.IsTransient(new FormatException()));
        }

        [Fact]
        public void MustLookAtInnerExceptions()
        {
            Assert.True(CdcTransientErrors.IsTransient(new InvalidOperationException("wrapped", new TimeoutException())));
            Assert.True(CdcTransientErrors.IsTransient(new AggregateException(new FormatException(), new IOException())));
            Assert.False(CdcTransientErrors.IsTransient(new AggregateException(new FormatException())));
        }

        [Fact]
        public void MustHonourAnIsTransientProperty()
        {
            Assert.True(CdcTransientErrors.IsTransient(new BrokerException(true)));
            Assert.False(CdcTransientErrors.IsTransient(new BrokerException(false)));
        }

        private sealed class BrokerException : Exception
        {
            public BrokerException(bool isTransient) => IsTransient = isTransient;
            public bool IsTransient { get; }
        }
    }
}
