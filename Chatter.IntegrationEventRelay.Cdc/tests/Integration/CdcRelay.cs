using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Integration
{
    /// <summary>
    /// A CDC source relaying a table's capture instance as the relay does, its poller running until stopped. Records the ids
    /// of the rows whose changes it relayed, failing to publish a change to row <see cref="FailOnId"/>.
    /// </summary>
    internal sealed class CdcRelay : IRowChangeProcessor<CdcRelay.Row>, IAsyncDisposable
    {
        private readonly ConcurrentQueue<int> _rowIds = new();
        private CancellationTokenSource _stop;
        private Task _run;

        public CdcCapturePoller<Row> Poller { get; private set; }

        public int? FailOnId { get; set; }

        public IReadOnlyList<int> RowIds => _rowIds.ToList();

        public static CdcRelay Start(SqlServerFixture.TestDatabase db, string captureInstance, string table, string sourceName, CdcErrorPolicy errorPolicy = CdcErrorPolicy.Halt, int? failOnId = null)
        {
            // A batch per transaction, so a change that fails to publish doesn't make the relay publish earlier ones again
            var options = new CdcSourceOptions { ErrorPolicy = errorPolicy, MaxAttemptsBeforePark = 1, MaxTransactionsPerBatch = 1 };
            options.PollInterval = options.LeaseRetryInterval = options.RetryDelay = TimeSpan.FromMilliseconds(200);
            options.MaxRetryDelay = TimeSpan.FromSeconds(1);
            var capture = new CdcCapture(sourceName, captureInstance, new SourceTable(table, "dbo", db.Name), typeof(Row), options);
            var relay = new CdcRelay { FailOnId = failOnId };
            var services = new ServiceCollection();
            services.AddSingleton<IRowChangeProcessor<Row>>(relay);
            relay.Poller = new CdcCapturePoller<Row>(capture, new SqlCdcQueries(db.ConnectionString), new SqlCheckpointStore(db.ConnectionString, db.RelaySchema),
                new SqlParkedChangeStore(db.ConnectionString, db.RelaySchema), new SqlAppLockLeaseFactory(db.ConnectionString),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<CdcCapturePoller<Row>>.Instance);
            relay.Resume();
            return relay;
        }

        public void Resume()
        {
            _stop = new CancellationTokenSource();
            _run = Poller.RunAsync(_stop.Token);
        }

        public async Task StopAsync()
        {
            _stop.Cancel();
            await _run;
        }

        public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
        {
            if (change.NewValue.Id == FailOnId)
                throw new InvalidOperationException("publish failed");
            _rowIds.Enqueue(change.NewValue.Id);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_run.IsCompleted)
                await StopAsync();
        }

        /// <summary>
        /// A row of a table created by <see cref="SqlServerFixture.TestDatabase.CreateTableAsync"/>.
        /// </summary>
        public sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool? Flag { get; set; }
            public DateTime? DeletedAt { get; set; }
        }
    }
}
