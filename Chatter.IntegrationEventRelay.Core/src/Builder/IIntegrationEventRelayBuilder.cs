using Chatter.CQRS.DependencyInjection;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.Core.Builder;

/// <summary>
/// Configures the integration event relay. Change source adapters extend it, for example <c>UseServiceBrokerChangeSource()</c>.
/// </summary>
public interface IIntegrationEventRelayBuilder
{
	IChatterBuilder ChatterBuilder { get; }
	IServiceCollection Services { get; }
	EventMappingConfiguration Configuration { get; }
}
