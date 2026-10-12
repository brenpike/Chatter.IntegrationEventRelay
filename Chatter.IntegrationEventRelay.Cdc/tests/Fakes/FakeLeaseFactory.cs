using Chatter.IntegrationEventRelay.Cdc.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Fakes
{
    internal sealed class FakeLeaseFactory : ICdcLeaseFactory
    {
        private readonly object _lock = new();
        private readonly List<FakeLease> _leases = new();
        private readonly List<string> _resources = new();

        public IReadOnlyList<FakeLease> Leases
        {
            get { lock (_lock) return _leases.ToList(); }
        }

        public IReadOnlyList<string> Resources
        {
            get { lock (_lock) return _resources.ToList(); }
        }

        /// <summary>Whether each lease created can be acquired. Default true.</summary>
        public Func<int, bool> CanAcquire { get; set; } = _ => true;

        /// <summary>Whether taking each lease created fails with an error (the database is unreachable). Default false.</summary>
        public Func<int, bool> AcquireFails { get; set; } = _ => false;

        /// <summary>How many lease checks each acquired lease passes before it's lost. Default: never lost.</summary>
        public int? HeldChecks { get; set; }

        public ICdcLease Create(string resource)
        {
            lock (_lock)
            {
                _resources.Add(resource);
                var lease = new FakeLease(CanAcquire(_leases.Count), HeldChecks, AcquireFails(_leases.Count));
                _leases.Add(lease);
                return lease;
            }
        }
    }

    internal sealed class FakeLease : ICdcLease
    {
        private readonly bool _canAcquire;
        private readonly bool _fails;
        private int? _heldChecks;

        public FakeLease(bool canAcquire, int? heldChecks, bool fails = false)
        {
            _canAcquire = canAcquire;
            _heldChecks = heldChecks;
            _fails = fails;
        }

        public bool Acquired { get; private set; }
        public bool Disposed { get; private set; }

        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
            => _fails ? Task.FromException<bool>(new TimeoutException("Login timeout expired")) : Task.FromResult(Acquired = _canAcquire);

        public Task<bool> IsHeldAsync(CancellationToken cancellationToken)
        {
            if (!Acquired || Disposed)
                return Task.FromResult(false);
            if (_heldChecks is null)
                return Task.FromResult(true);
            return Task.FromResult(_heldChecks-- > 0);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
