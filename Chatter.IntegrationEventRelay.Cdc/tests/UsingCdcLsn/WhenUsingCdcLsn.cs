using System;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcLsn
{
    public class WhenUsingCdcLsn
    {
        [Fact]
        public void MustRoundTripItsHexForm()
        {
            var lsn = CdcLsn.Parse("0x0000002A000001F00003");

            Assert.Equal("0x0000002A000001F00003", lsn.ToString());
            Assert.Equal(lsn, CdcLsn.Parse("0000002a000001f00003"));
        }

        [Fact]
        public void MustRoundTripItsBytes()
        {
            var bytes = new byte[] { 0, 0, 0, 0x2A, 0, 0, 0x01, 0xF0, 0, 0x03 };

            Assert.Equal(bytes, CdcLsn.FromBytes(bytes).ToArray());
        }

        [Theory]
        [InlineData("0x00000000000000000001", "0x00000000000000000002")]
        [InlineData("0x0000000000000000FFFF", "0x00000000000000010000")]
        [InlineData("0x000000000000FFFFFFFF", "0x00000000000100000000")]
        [InlineData("0x00FFFFFFFFFFFFFFFFFF", "0x01000000000000000000")]
        public void MustIncrementWithCarry(string value, string expected)
            => Assert.Equal(CdcLsn.Parse(expected), CdcLsn.Parse(value).Increment());

        [Theory]
        [InlineData("0x00000000000000000002", "0x00000000000000000001")]
        [InlineData("0x00000000000000010000", "0x0000000000000000FFFF")]
        [InlineData("0x01000000000000000000", "0x00FFFFFFFFFFFFFFFFFF")]
        public void MustDecrementWithBorrow(string value, string expected)
            => Assert.Equal(CdcLsn.Parse(expected), CdcLsn.Parse(value).Decrement());

        [Fact]
        public void MustNotDecrementBelowZero()
            => Assert.Throws<OverflowException>(() => CdcLsn.Zero.Decrement());

        [Fact]
        public void MustNotIncrementPastTheLargestValue()
            => Assert.Throws<OverflowException>(() => CdcLsn.Parse("0xFFFFFFFFFFFFFFFFFFFF").Increment());

        [Theory]
        [InlineData("0x00000000000000000001", "0x00000000000000000002")]
        [InlineData("0x0000000000000000FFFF", "0x00000000000000010000")]
        [InlineData("0x7F000000000000000000", "0x80000000000000000000")]
        public void MustCompareAsAnUnsignedBigEndianNumber(string smaller, string larger)
        {
            var a = CdcLsn.Parse(smaller);
            var b = CdcLsn.Parse(larger);

            Assert.True(a < b);
            Assert.True(b > a);
            Assert.True(a <= b);
            Assert.True(a != b);
            Assert.Equal(-1, Math.Sign(a.CompareTo(b)));
            Assert.Equal(1, Math.Sign(((IComparable)b).CompareTo(a)));
        }

        [Fact]
        public void MustBeZeroByDefault()
        {
            Assert.True(default(CdcLsn).IsZero);
            Assert.Equal(CdcLsn.Zero, CdcLsn.Parse("0x00000000000000000000"));
            Assert.False(CdcLsn.Parse("0x00000000000000000001").IsZero);
        }

        [Theory]
        [InlineData("0x0001")]
        [InlineData("0x0000000000000000000001")]
        public void MustRejectValuesThatAreNotTenBytes(string value)
            => Assert.Throws<FormatException>(() => CdcLsn.Parse(value));

        [Fact]
        public void MustRejectByteArraysThatAreNotTenBytes()
            => Assert.Throws<ArgumentException>(() => CdcLsn.FromBytes(new byte[8]));
    }
}
