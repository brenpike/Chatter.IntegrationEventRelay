using Microsoft.Extensions.Hosting;

namespace Chatter.IntegrationEventRelay.Cdc.Polling;

/// <summary>
/// Runs a poll loop for every configured capture instance.
/// </summary>
internal sealed class CdcPollerService : BackgroundService
{
	private readonly IEnumerable<ICdcCapturePoller> _pollers;

	public CdcPollerService(IEnumerable<ICdcCapturePoller> pollers) => _pollers = pollers ?? throw new ArgumentNullException(nameof(pollers));

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
		=> Task.WhenAll(_pollers.Select(p => Task.Run(() => p.RunAsync(stoppingToken), CancellationToken.None)));
}
