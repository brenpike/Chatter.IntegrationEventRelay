using Chatter.IntegrationEventRelay.Cdc.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Fakes
{
    internal sealed class FakeCheckpointStore : ICdcCheckpointStore
    {
        private readonly object _lock = new();
        private readonly Dictionary<(string, string), CdcLsn> _checkpoints = new();
        private readonly List<CdcLsn> _saves = new();

        public IReadOnlyList<CdcLsn> Saves
        {
            get { lock (_lock) return _saves.ToList(); }
        }

        public CdcLsn? this[string sourceName, string captureInstance]
        {
            get
            {
                lock (_lock)
                    return _checkpoints.TryGetValue((sourceName, captureInstance), out var lsn) ? lsn : null;
            }
            set
            {
                lock (_lock)
                {
                    if (value is { } lsn)
                        _checkpoints[(sourceName, captureInstance)] = lsn;
                    else
                        _checkpoints.Remove((sourceName, captureInstance));
                }
            }
        }

        public Task<CdcLsn?> GetAsync(string sourceName, string captureInstance, CancellationToken cancellationToken)
            => Task.FromResult(this[sourceName, captureInstance]);

        public Task SaveAsync(string sourceName, string captureInstance, CdcLsn lastLsn, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _checkpoints[(sourceName, captureInstance)] = lastLsn;
                _saves.Add(lastLsn);
            }
            return Task.CompletedTask;
        }
    }
}
