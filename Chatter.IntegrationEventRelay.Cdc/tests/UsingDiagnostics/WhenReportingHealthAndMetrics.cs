using Chatter.IntegrationEventRelay.Cdc.Diagnostics;
using Chatter.IntegrationEventRelay.Cdc.Extensions;
using Chatter.IntegrationEventRelay.Cdc.Polling;
using Chatter.IntegrationEventRelay.Cdc.Sql;
using Chatter.IntegrationEventRelay.Cdc.Tests.Fakes;
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Chatter.IntegrationEventRelay.Cdc.Tests.UsingDiagnostics
{
    public class WhenReportingHealthAndMetrics
    {
        private readonly PollerHarness<Row> _harness = new(new CdcSourceOptions { ErrorPolicy = CdcErrorPolicy.ParkRow, MaxAttemptsBeforePark = 1, FailuresBeforeCritical = 2 });
        private readonly Processor _processor = new();

        public WhenReportingHealthAndMetrics() => _harness.Processor = _processor;

        [Fact]
        public async Task MustReportLagFromTheLastRelayedChangeAndZeroOnceCaughtUp()
        {
            Assert.Null(_harness.Poller.GetLag(DateTimeOffset.UtcNow));
            var transaction = Insert(1, "a");
            var committedAt = new DateTimeOffset(transaction.CommitTime, _harness.Queries.ServerUtcOffset);

            await PollAsync();

            Assert.Equal(TimeSpan.FromMinutes(3), _harness.Poller.GetLag(committedAt.AddMinutes(3)));
            await PollAsync();
            Assert.Equal(TimeSpan.Zero, _harness.Poller.GetLag(committedAt.AddMinutes(3)));
        }

        [Fact]
        public async Task MustCountParkedChanges()
        {
            await PollAsync();
            Assert.Equal(0, _harness.Poller.ParkedChanges);

            Insert(1, "poison");
            await PollAsync();

            Assert.Equal(1, _harness.Poller.ParkedChanges);
        }

        [Fact]
        public async Task MustBeHealthyWhileRelaying()
        {
            Insert(1, "a");
            await PollAsync();

            Assert.Equal(HealthStatus.Healthy, (await CheckAsync(StubPoller.From(_harness.Poller, CdcPollerState.Active))).Status);
        }

        [Fact]
        public async Task MustBeDegradedWithParkedChanges()
        {
            Insert(1, "poison");
            await PollAsync();

            var result = await CheckAsync(StubPoller.From(_harness.Poller, CdcPollerState.Active));

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("parked", result.Description);
        }

        [Theory]
        [InlineData("Retrying", 1, HealthStatus.Degraded)]
        [InlineData("Retrying", 2, HealthStatus.Unhealthy)]
        [InlineData("Gap", 1, HealthStatus.Unhealthy)]
        [InlineData("Standby", 0, HealthStatus.Healthy)]
        [InlineData("Starting", 0, HealthStatus.Healthy)]
        public async Task MustReflectThePollersState(string state, int failures, HealthStatus expected)
            => Assert.Equal(expected, (await CheckAsync(new StubPoller(_harness.Capture, Enum.Parse<CdcPollerState>(state), failures, null, null))).Status);

        [Fact]
        public async Task MustBeUnhealthyWhenLaggingTooFarBehind()
        {
            var result = await CheckAsync(new StubPoller(_harness.Capture, CdcPollerState.Active, 0, TimeSpan.FromMinutes(10), null), TimeSpan.FromMinutes(5));

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("behind", result.Description);
        }

        [Theory]
        [InlineData("Standby")]
        [InlineData("Starting")]
        public async Task MustIgnoreLagAndParkedChangesLeftOnAnInstanceWithoutTheLease(string state)
        {
            var result = await CheckAsync(new StubPoller(_harness.Capture, Enum.Parse<CdcPollerState>(state), 0, TimeSpan.FromHours(1), 3));

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public void MustRegisterTheHealthCheck()
        {
            var services = new ServiceCollection();
            services.AddHealthChecks().AddCdcChangeSource(tags: new[] { "ready" });

            var registration = Assert.Single(services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations);

            Assert.Equal("cdc", registration.Name);
            Assert.Contains("ready", registration.Tags);
        }

        [Fact]
        public async Task MustMeasureRelayedAndParkedChanges()
        {
            var measurements = new ConcurrentBag<(string Name, double Value, string CaptureInstance)>();
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == CdcMetrics.MeterName)
                        l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((i, v, tags, _) => measurements.Add((i.Name, v, CaptureInstance(tags))));
            listener.SetMeasurementEventCallback<double>((i, v, tags, _) => measurements.Add((i.Name, v, CaptureInstance(tags))));
            listener.Start();

            Insert(1, "a");
            Insert(2, "poison");
            await PollAsync();

            var mine = measurements.Where(m => m.CaptureInstance == _harness.Capture.CaptureInstance).ToList();
            Assert.Contains(mine, m => m.Name == "chatter.relay.cdc.changes" && m.Value == 2);
            Assert.Contains(mine, m => m.Name == "chatter.relay.cdc.parked" && m.Value == 1);
            Assert.Contains(mine, m => m.Name == "chatter.relay.cdc.batch.duration");
        }

        private static string CaptureInstance(ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            foreach (var tag in tags)
                if (tag.Key == "capture_instance")
                    return (string)tag.Value;
            return null;
        }

        private Task<HealthCheckResult> CheckAsync(ICdcCapturePoller poller, TimeSpan? maxLag = null)
            => new CdcHealthCheck(new[] { poller }, TimeProvider.System, maxLag ?? TimeSpan.FromMinutes(5)).CheckHealthAsync(new HealthCheckContext());

        private Task<bool> PollAsync() => _harness.Poller.PollOnceAsync(CancellationToken.None);

        private FakeTransaction Insert(int id, string name) => _harness.Queries.Commit((CdcChangeRow.Insert, PollerHarness<Row>.Columns(id, name)));

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class Processor : IRowChangeProcessor<Row>
        {
            public Task ProcessAsync(RowChange<Row> change, CancellationToken cancellationToken)
                => change.NewValue.Name == "poison" ? throw new InvalidOperationException("poison") : Task.CompletedTask;
        }

        private sealed class StubPoller : ICdcCapturePoller
        {
            private readonly TimeSpan? _lag;

            public StubPoller(CdcCapture capture, CdcPollerState state, int failures, TimeSpan? lag, int? parked)
            {
                Capture = capture;
                State = state;
                ConsecutiveFailures = failures;
                _lag = lag;
                ParkedChanges = parked;
            }

            // The fake commit times are in the past, so lag is left out
            public static StubPoller From(ICdcCapturePoller poller, CdcPollerState state)
                => new(poller.Capture, state, poller.ConsecutiveFailures, TimeSpan.Zero, poller.ParkedChanges);

            public CdcCapture Capture { get; }
            public CdcPollerState State { get; }
            public int ConsecutiveFailures { get; }
            public int? ParkedChanges { get; }
            public TimeSpan? GetLag(DateTimeOffset now) => _lag;
            public Task RunAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        }
    }
}
