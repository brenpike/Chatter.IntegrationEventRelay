using Chatter.IntegrationEventRelay.Cdc.Diagnostics;
using Chatter.IntegrationEventRelay.Cdc.Polling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Chatter.IntegrationEventRelay.Cdc.Extensions;

public static class HealthChecksBuilderExtensions
{
	/// <summary>
	/// Adds a health check of this relay instance's CDC pollers.
	/// </summary>
	/// <param name="maxLag">The most time since the last relayed change was committed before the check is unhealthy. Default 5 minutes.</param>
	public static IHealthChecksBuilder AddCdcChangeSource(this IHealthChecksBuilder builder, string name = "cdc", TimeSpan? maxLag = null, IEnumerable<string>? tags = null)
		=> builder.Add(new HealthCheckRegistration(name,
			sp => new CdcHealthCheck(sp.GetServices<ICdcCapturePoller>(), sp.GetService<TimeProvider>() ?? TimeProvider.System, maxLag ?? TimeSpan.FromMinutes(5)),
			failureStatus: null, tags));
}
