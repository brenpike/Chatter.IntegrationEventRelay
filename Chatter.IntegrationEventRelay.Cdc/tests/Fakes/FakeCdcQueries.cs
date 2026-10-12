using Chatter.IntegrationEventRelay.Cdc.Sql;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Fakes
{
    /// <summary>
    /// An in-memory CDC capture instance: committed transactions with their change rows, read the way the CDC functions read them.
    /// </summary>
    internal sealed class FakeCdcQueries : ICdcQueries
    {
        private ulong _nextLsn = 0x100;
        private readonly object _lock = new();
        private readonly List<FakeTransaction> _transactions = new();

        public static CdcLsn Lsn(ulong value)
        {
            var bytes = new byte[CdcLsn.Length];
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(2), value);
            return CdcLsn.FromBytes(bytes);
        }

        /// <summary>When CDC was enabled on the table; the oldest LSN until cleanup moves it.</summary>
        public CdcLsn MinLsn { get; set; } = Lsn(0x10);
        public bool CaptureInstanceExists { get; set; } = true;
        public TimeSpan ServerUtcOffset { get; set; } = TimeSpan.FromHours(-5);
        public DateTime NextCommitTime { get; set; } = new(2026, 10, 12, 8, 0, 0);
        public List<string> CapturedColumns { get; } = new() { "Id", "Name" };
        public List<string> KeyColumns { get; } = new() { "Id" };
        public List<(CdcLsn From, CdcLsn To)> ChangeReads { get; } = new();
        public List<int> BatchSizes { get; } = new();
        public Exception FailWith { get; set; }

        public IReadOnlyList<FakeTransaction> Transactions
        {
            get { lock (_lock) return _transactions.ToList(); }
        }

        /// <summary>Commits a transaction made of <paramref name="rows"/>; an update is a before and an after row.</summary>
        public FakeTransaction Commit(params (int Operation, IReadOnlyDictionary<string, object> Columns)[] rows)
        {
            lock (_lock)
                return CommitLocked(rows);
        }

        /// <summary>
        /// Commits a primary key update as SQL Server's CDC records it: a delete of the old row and an insert of the new
        /// one, with the same sequence value.
        /// </summary>
        public FakeTransaction CommitPrimaryKeyUpdate(IReadOnlyDictionary<string, object> oldRow, IReadOnlyDictionary<string, object> newRow)
        {
            lock (_lock)
            {
                var transaction = CommitLocked(new[] { (CdcChangeRow.Delete, oldRow), (CdcChangeRow.Insert, newRow) });
                var insert = transaction.Rows[1] with { SeqVal = transaction.Rows[0].SeqVal };
                var updated = transaction with { Rows = new[] { transaction.Rows[0], insert } };
                _transactions[^1] = updated;
                return updated;
            }
        }

        private FakeTransaction CommitLocked((int Operation, IReadOnlyDictionary<string, object> Columns)[] rows)
        {
            var lsn = Lsn(_nextLsn += 0x10);
            var seqVal = 0UL;
            var changeRows = new List<CdcChangeRow>();
            foreach (var (operation, columns) in rows)
            {
                // An update's after image shares its before image's sequence value
                if (operation != CdcChangeRow.UpdateAfter)
                    seqVal++;
                changeRows.Add(new CdcChangeRow(lsn, Lsn(seqVal), operation, NextCommitTime, new Dictionary<string, object>(columns, StringComparer.OrdinalIgnoreCase)));
            }

            var transaction = new FakeTransaction(lsn, NextCommitTime, changeRows);
            lock (_lock)
                _transactions.Add(transaction);
            NextCommitTime = NextCommitTime.AddSeconds(1);
            return transaction;
        }

        /// <summary>An empty <c>cdc.lsn_time_mapping</c> entry (tran_id 0x00) the capture job writes while the database is idle.</summary>
        public FakeTransaction CommitHeartbeat()
        {
            lock (_lock)
            {
                var transaction = new FakeTransaction(Lsn(_nextLsn += 0x10), NextCommitTime, new List<CdcChangeRow>(), IsHeartbeat: true);
                _transactions.Add(transaction);
                return transaction;
            }
        }

        public Task<CdcLsn> GetMinLsnAsync(string captureInstance, CancellationToken cancellationToken)
            => Result(CaptureInstanceExists ? MinLsn : CdcLsn.Zero);

        public Task<bool> CaptureInstanceExistsAsync(string captureInstance, CancellationToken cancellationToken)
            => Result(CaptureInstanceExists);

        public Task<CdcLsn> GetMaxLsnAsync(CancellationToken cancellationToken)
        {
            var transactions = Transactions;
            return Result(transactions.Count == 0 ? CdcLsn.Zero : transactions[^1].Lsn);
        }

        public Task<CdcLsn?> GetBatchUpperBoundAsync(CdcLsn from, CdcLsn max, int maxTransactions, CancellationToken cancellationToken)
        {
            BatchSizes.Add(maxTransactions);
            var batch = Transactions.Where(t => !t.IsHeartbeat && t.Lsn >= from && t.Lsn <= max).Take(maxTransactions).ToList();
            return Result(batch.Count == 0 ? (CdcLsn?)null : batch[^1].Lsn);
        }

        public Task<IReadOnlyList<CdcChangeRow>> GetChangesAsync(string captureInstance, CdcLsn from, CdcLsn to, CancellationToken cancellationToken)
        {
            ChangeReads.Add((from, to));
            if (from < MinLsn || from > to)
                throw new InvalidOperationException("An insufficient number of arguments were supplied for the procedure or function cdc.fn_cdc_get_all_changes_ ...");

            return Result<IReadOnlyList<CdcChangeRow>>(Transactions.Where(t => t.Lsn >= from && t.Lsn <= to).SelectMany(t => t.Rows).ToList());
        }

        public Task<TimeSpan> GetServerUtcOffsetAsync(CancellationToken cancellationToken) => Result(ServerUtcOffset);

        public Task<IReadOnlyList<string>> GetCapturedColumnsAsync(string captureInstance, CancellationToken cancellationToken)
            => Result<IReadOnlyList<string>>(CaptureInstanceExists ? CapturedColumns.ToList() : new List<string>());

        public Task<IReadOnlyList<string>> GetKeyColumnsAsync(string captureInstance, CancellationToken cancellationToken)
            => Result<IReadOnlyList<string>>(KeyColumns.ToList());

        private Task<T> Result<T>(T value) => FailWith is null ? Task.FromResult(value) : Task.FromException<T>(FailWith);
    }

    internal sealed record FakeTransaction(CdcLsn Lsn, DateTime CommitTime, IReadOnlyList<CdcChangeRow> Rows, bool IsHeartbeat = false);
}
