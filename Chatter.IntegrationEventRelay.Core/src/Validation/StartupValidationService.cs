using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chatter.IntegrationEventRelay.Core.Validation;

/// <summary>
/// Runs every <see cref="IRelayStartupCheck"/> when the host starts and stops startup if any fails, so a misconfigured
/// relay fails fast instead of silently dropping changes.
/// </summary>
public class StartupValidationService : IHostedService
{
	private readonly IServiceProvider _services;
	private readonly ILogger<StartupValidationService> _logger;

	public StartupValidationService(IServiceProvider services, ILogger<StartupValidationService> logger)
	{
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		using var scope = _services.CreateScope();
		var problems = scope.ServiceProvider.GetServices<IRelayStartupCheck>()
			.SelectMany(c => c.Validate(scope.ServiceProvider))
			.ToList();

		if (problems.Count > 0)
		{
			var message = "Integration event relay configuration is invalid:" + string.Concat(problems.Select(p => $"{Environment.NewLine} - {p}"));
			_logger.LogCritical("{ValidationErrors}", message);
			throw new InvalidOperationException(message);
		}

		_logger.LogInformation("Integration event relay configuration is valid");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
