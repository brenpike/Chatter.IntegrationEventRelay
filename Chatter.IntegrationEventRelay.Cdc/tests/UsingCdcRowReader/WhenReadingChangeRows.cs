using Chatter.IntegrationEventRelay.Cdc.Reading;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Cdc.Tests.Fakes;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingCdcRowReader
{
    public class WhenReadingChangeRows
    {
        private readonly FakeCdcQueries _cdc = new();
        private readonly RecordingLogger<WhenReadingChangeRows> _logger = new();
        private readonly CdcCapture _capture = new("fakedb-cdc", "dbo_Event1", new SourceTable("Event1", "dbo", "FakeDb"), typeof(Row), new CdcSourceOptions());
        private readonly CdcRowReader<Row> _sut;

        public WhenReadingChangeRows() => _sut = new CdcRowReader<Row>(_logger);

        [Fact]
        public void MustReadInsertsAndDeletes()
        {
            _cdc.Commit((CdcChangeRow.Insert, Columns(1, "a")), (CdcChangeRow.Delete, Columns(2, "b")));

            var changes = Read();

            Assert.Equal(new[] { RowOperation.Insert, RowOperation.Delete }, changes.Select(c => c.Operation));
            Assert.All(changes, c => Assert.Null(c.OldValue));
            Assert.Equal(new[] { 1, 2 }, changes.Select(c => c.NewValue.Id));
        }

        [Fact]
        public void MustPairBeforeAndAfterImagesIntoOneUpdate()
        {
            _cdc.Commit((CdcChangeRow.UpdateBefore, Columns(1, "before")), (CdcChangeRow.UpdateAfter, Columns(1, "after")),
                (CdcChangeRow.UpdateBefore, Columns(2, "before")), (CdcChangeRow.UpdateAfter, Columns(2, "after")));

            var changes = Read();

            Assert.Equal(2, changes.Count);
            Assert.All(changes, c => Assert.Equal(RowOperation.Update, c.Operation));
            Assert.All(changes, c => Assert.Equal("before", c.OldValue.Name));
            Assert.All(changes, c => Assert.Equal("after", c.NewValue.Name));
            Assert.Equal(new[] { 1, 2 }, changes.Select(c => c.NewValue.Id));
        }

        [Fact]
        public void MustRejectABeforeImageWithoutItsAfterImage()
        {
            _cdc.Commit((CdcChangeRow.UpdateBefore, Columns(1, "before")), (CdcChangeRow.Insert, Columns(2, "b")));

            Assert.Throws<InvalidOperationException>(() => Read());
        }

        [Fact]
        public void MustRejectAnAfterImageWithoutItsBeforeImage()
        {
            _cdc.Commit((CdcChangeRow.UpdateAfter, Columns(1, "after")));

            Assert.Throws<InvalidOperationException>(() => Read());
        }

        [Fact]
        public void MustRejectUnknownOperations()
        {
            _cdc.Commit((5, Columns(1, "a")));

            Assert.Throws<InvalidOperationException>(() => Read());
        }

        [Fact]
        public void MustDescribeEachChangeWithTheCapture()
        {
            _cdc.Commit((CdcChangeRow.Insert, Columns(1, "a")));

            var change = Assert.Single(Read());

            Assert.Equal("fakedb-cdc", change.SourceName);
            Assert.Equal(_capture.Table, change.Table);
        }

        [Fact]
        public void MustConvertCommitTimesWithTheServersOffset()
        {
            _cdc.NextCommitTime = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Unspecified);
            _cdc.Commit((CdcChangeRow.Insert, Columns(1, "a")));

            var change = Assert.Single(_sut.Read(Rows(), _capture, TimeSpan.FromHours(-4)));

            Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.FromHours(-4)), change.CommittedAt);
        }

        [Fact]
        public void MustGiveEachChangeAStablePositionOfCaptureInstanceLsnSequenceAndOperation()
        {
            var transaction = _cdc.Commit((CdcChangeRow.Insert, Columns(1, "a")), (CdcChangeRow.UpdateBefore, Columns(1, "a")), (CdcChangeRow.UpdateAfter, Columns(1, "b")));

            var first = Read();
            var second = Read();

            Assert.Equal($"dbo_Event1:{transaction.Lsn}:{FakeCdcQueries.Lsn(1)}:2", first[0].Position.Value);
            Assert.Equal($"dbo_Event1:{transaction.Lsn}:{FakeCdcQueries.Lsn(2)}:4", first[1].Position.Value);
            Assert.Equal(first.Select(c => c.Position), second.Select(c => c.Position));
            Assert.True(first[0].Position.Order.CompareTo(first[1].Position.Order) < 0);
        }

        [Fact]
        public void MustKeepPositionsUniqueIfRowsShareThem()
        {
            var lsn = FakeCdcQueries.Lsn(0x20);
            var rows = new[]
            {
                new CdcChangeRow(lsn, FakeCdcQueries.Lsn(1), CdcChangeRow.Insert, null, Columns(1, "a")),
                new CdcChangeRow(lsn, FakeCdcQueries.Lsn(1), CdcChangeRow.Insert, null, Columns(2, "b"))
            };

            var changes = _sut.Read(rows, _capture, TimeSpan.Zero);

            Assert.NotEqual(changes[0].Position.Value, changes[1].Position.Value);
        }

        [Fact]
        public void MustSetPropertiesFromColumnsIgnoringCase()
        {
            _cdc.Commit((CdcChangeRow.Insert, new Dictionary<string, object> { ["ID"] = 7, ["name"] = "a", ["Status"] = 2, ["Unmapped"] = "x" }));

            var row = Assert.Single(Read()).NewValue;

            Assert.Equal(7, row.Id);
            Assert.Equal("a", row.Name);
            Assert.Equal(Status.Deleted, row.Status);
        }

        [Fact]
        public void MustUseTheDefaultForANullColumnOfANonNullablePropertyAndWarnOnce()
        {
            _cdc.Commit((CdcChangeRow.Insert, new Dictionary<string, object> { ["Id"] = null, ["Name"] = null, ["DeletedBy"] = null }));
            _cdc.Commit((CdcChangeRow.Insert, new Dictionary<string, object> { ["Id"] = null }));

            var changes = Read();

            Assert.All(changes, c => Assert.Equal(0, c.NewValue.Id));
            Assert.Null(changes[0].NewValue.Name);
            Assert.Null(changes[0].NewValue.DeletedBy);
            Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        }

        [Fact]
        public void MustConvertCompatibleColumnTypes()
        {
            _cdc.Commit((CdcChangeRow.Insert, new Dictionary<string, object> { ["Id"] = (short)3, ["DeletedBy"] = 4L, ["Status"] = "deleted" }));

            var row = Assert.Single(Read()).NewValue;

            Assert.Equal(3, row.Id);
            Assert.Equal(4, row.DeletedBy);
            Assert.Equal(Status.Deleted, row.Status);
        }

        [Fact]
        public void MustExplainAColumnThatCantSetItsProperty()
        {
            _cdc.Commit((CdcChangeRow.Insert, new Dictionary<string, object> { ["Id"] = "not a number" }));

            var e = Assert.Throws<InvalidOperationException>(() => Read());

            Assert.Contains("Row.Id", e.Message);
        }

        [Fact]
        public void MustListPropertiesNoCapturedColumnSets()
            => Assert.Equal(new[] { "DeletedBy", "Status" }, _sut.GetUnmappedProperties(new[] { "id", "NAME", "Other" }));

        private IReadOnlyList<RowChange<Row>> Read() => _sut.Read(Rows(), _capture, _cdc.ServerUtcOffset);

        private IReadOnlyList<CdcChangeRow> Rows() => _cdc.Transactions.SelectMany(t => t.Rows).ToList();

        private static IReadOnlyDictionary<string, object> Columns(int id, string name) => PollerHarness<Row>.Columns(id, name);

        public enum Status { Active = 1, Deleted = 2 }

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int? DeletedBy { get; set; }
            public Status Status { get; set; }
            public string ReadOnly => Name;
        }
    }
}
