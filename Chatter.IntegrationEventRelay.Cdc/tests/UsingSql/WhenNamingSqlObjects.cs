using Chatter.IntegrationEventRelay.Cdc.Sql;
using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingSql
{
    public class WhenNamingSqlObjects
    {
        [Theory]
        [InlineData("Chatter", "[Chatter]")]
        [InlineData("odd]name", "[odd]]name]")]
        public void MustDelimitIdentifiers(string name, string expected)
            => Assert.Equal(expected, SqlIdentifier.Quote(name));

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void MustRejectEmptyIdentifiers(string name)
            => Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(name));

        [Fact]
        public void MustRejectIdentifiersLongerThanSqlServerAllows()
            => Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(new string('a', 129)));

        [Fact]
        public void MustKeepShortLockNames()
            => Assert.Equal("Chatter.IntegrationEventRelay.Cdc:src:dbo_Event1", SqlAppLockLease.ToResourceName("Chatter.IntegrationEventRelay.Cdc:src:dbo_Event1"));

        [Fact]
        public void MustShortenLongLockNamesKeepingThemDistinct()
        {
            var a = SqlAppLockLease.ToResourceName(new string('a', 300) + "1");
            var b = SqlAppLockLease.ToResourceName(new string('a', 300) + "2");

            Assert.Equal(255, a.Length);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void MustCreateTheCheckpointTableInTheRelaySchema()
        {
            Assert.Contains("QUOTENAME(@schema) + N'.[Chatter_RelayCheckpoints]", CdcSqlScripts.CreateRelayTables);
            Assert.DoesNotContain("EXEC (", CdcSqlScripts.CreateRelayTables);
            Assert.Contains("LastLsn binary(10)", CdcSqlScripts.CreateRelayTables);
        }
    }
}
