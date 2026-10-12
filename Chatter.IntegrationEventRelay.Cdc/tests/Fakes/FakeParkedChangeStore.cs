using Chatter.IntegrationEventRelay.Cdc.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Fakes
{
    internal sealed class FakeParkedChangeStore : ICdcParkedChangeStore
    {
        private readonly object _lock = new();
        private readonly List<(ParkedChange Change, bool ReplayRequested)> _changes = new();
        private long _nextId;

        public IReadOnlyList<ParkedChange> Changes
        {
            get { lock (_lock) return _changes.Select(c => c.Change).ToList(); }
        }

        /// <summary>What an operator does to replay a row: flag its parked changes.</summary>
        public void RequestReplay(string rowKey)
        {
            lock (_lock)
                for (var i = 0; i < _changes.Count; i++)
                    if (_changes[i].Change.RowKey == rowKey)
                        _changes[i] = (_changes[i].Change, true);
        }

        public bool IsReplayRequested(string rowKey)
        {
            lock (_lock) return _changes.Any(c => c.Change.RowKey == rowKey && c.ReplayRequested);
        }

        public Task<IReadOnlySet<string>> GetParkedRowKeysAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
        {
            lock (_lock) return Task.FromResult<IReadOnlySet<string>>(_changes.Select(c => c.Change.RowKey).ToHashSet());
        }

        public Task ParkAsync(string sourceName, string captureInstance, ParkedChange change, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                // Like the table's unique constraint on position
                if (!_changes.Any(c => c.Change.Position == change.Position))
                    _changes.Add((change with { Id = ++_nextId }, false));
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRowKeysToReplayAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
        {
            lock (_lock) return Task.FromResult<IReadOnlyList<string>>(_changes.Where(c => c.ReplayRequested).Select(c => c.Change.RowKey).Distinct().ToList());
        }

        public Task<IReadOnlyList<ParkedChange>> GetParkedChangesAsync(string sourceName, string captureInstance, string rowKey, CancellationToken cancellationToken)
        {
            lock (_lock)
                return Task.FromResult<IReadOnlyList<ParkedChange>>(_changes.Select(c => c.Change).Where(c => c.RowKey == rowKey)
                    .OrderBy(c => c.Lsn).ThenBy(c => c.SeqVal).ThenBy(c => c.Id).ToList());
        }

        public Task RemoveAsync(long id, CancellationToken cancellationToken)
        {
            lock (_lock) _changes.RemoveAll(c => c.Change.Id == id);
            return Task.CompletedTask;
        }

        public Task RecordReplayFailureAsync(string sourceName, string captureInstance, ParkedChange change, string error, CancellationToken cancellationToken)
        {
            lock (_lock)
                for (var i = 0; i < _changes.Count; i++)
                {
                    var current = _changes[i].Change;
                    if (current.RowKey != change.RowKey)
                        continue;
                    _changes[i] = (current.Id == change.Id ? current with { Error = error, Attempts = current.Attempts + 1 } : current, false);
                }
            return Task.CompletedTask;
        }

        public Task<(int Count, DateTime? OldestParkedAt)> GetSummaryAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
        {
            lock (_lock) return Task.FromResult((_changes.Count, _changes.Count == 0 ? (DateTime?)null : new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc)));
        }
    }
}
