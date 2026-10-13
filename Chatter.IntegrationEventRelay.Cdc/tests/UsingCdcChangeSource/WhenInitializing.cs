using Chatter.IntegrationEventRelay.Core.Abstractions;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcChangeSource
{
    public class WhenInitializing
    {
        [Fact]
        public void MustExposeItsName()
            => Assert.Equal("fakedb-cdc", new CdcChangeSource("fakedb-cdc").Name);

        [Fact]
        public void MustBeOfTypeCdc()
            => Assert.Equal("Cdc", new CdcChangeSource("fakedb-cdc").Type);

        [Fact]
        public void MustBeOrderedResumableAndReportCommitTimes()
            => Assert.Equal(ChangeSourceCapabilities.Ordered | ChangeSourceCapabilities.Resumable | ChangeSourceCapabilities.CommitTime, new CdcChangeSource("fakedb-cdc").Capabilities);

        [Fact]
        public void MustNotClaimCommitOrderWhenParkingRows()
            => Assert.Equal(ChangeSourceCapabilities.Resumable | ChangeSourceCapabilities.CommitTime, new CdcChangeSource("fakedb-cdc", CdcErrorPolicy.ParkRow).Capabilities);

        [Fact]
        public void MustShareOneLeaseHoweverTheNamesAreSpelled()
        {
            var table = new SourceTable("Event1", "dbo", "FakeDb");
            var a = new CdcCapture("fakedb-cdc", "dbo_Event1", table, typeof(object), new CdcSourceOptions());
            var b = new CdcCapture("FakeDb-CDC", "DBO_event1", table, typeof(object), new CdcSourceOptions());

            Assert.Equal(a.LeaseResource, b.LeaseResource);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void MustThrowWithoutAName(string? name)
            => Assert.Throws<ArgumentNullException>(() => new CdcChangeSource(name!));
    }
}
