using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.Fakes
{
    /// <summary>
    /// A <see cref="CdcCapturePoller{TSource}"/> over in-memory CDC, checkpoints and leases, whose processor is resolved from a scope per batch.
    /// </summary>
    internal sealed class PollerHarness<TSource> where TSource : class, new()
    {
        public const string SourceName = "fakedb-cdc";
        public const string CaptureInstance = "dbo_Event1";

        private readonly ServiceProvider _services;

        public PollerHarness(CdcSourceOptions options = null)
        {
            Options = options ?? new CdcSourceOptions { PollInterval = TimeSpan.FromMilliseconds(5), RetryDelay = TimeSpan.FromMilliseconds(5), MaxRetryDelay = TimeSpan.FromMilliseconds(20), LeaseRetryInterval = TimeSpan.FromMilliseconds(5) };
            Capture = new CdcCapture(SourceName, CaptureInstance, new SourceTable("Event1", "dbo", "FakeDb"), typeof(TSource), Options);

            var services = new ServiceCollection();
            services.AddScoped<IRowChangeProcessor<TSource>>(_ =>
            {
                Scopes++;
                return Processor ?? throw new InvalidOperationException("No processor set");
            });
            _services = services.BuildServiceProvider();

            Poller = new CdcCapturePoller<TSource>(Capture, Queries, Checkpoints, Parked, Leases, _services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, Logger);
        }

        public CdcSourceOptions Options { get; }
        public CdcCapture Capture { get; }
        public FakeCdcQueries Queries { get; } = new();
        public FakeCheckpointStore Checkpoints { get; } = new();
        public FakeParkedChangeStore Parked { get; } = new();
        public FakeLeaseFactory Leases { get; } = new();
        public RecordingLogger<CdcCapturePoller<TSource>> Logger { get; } = new();
        public CdcCapturePoller<TSource> Poller { get; }
        public IRowChangeProcessor<TSource> Processor { get; set; }
        public int Scopes { get; private set; }

        public CdcLsn? Checkpoint
        {
            get => Checkpoints[SourceName, CaptureInstance];
            set => Checkpoints[SourceName, CaptureInstance] = value;
        }

        public static IReadOnlyDictionary<string, object> Columns(int id, string name) => new Dictionary<string, object> { ["Id"] = id, ["Name"] = name };
    }
}
