using Chatter.CQRS.DependencyInjection;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.Core.Builder;

internal sealed class IntegrationEventRelayBuilder : IIntegrationEventRelayBuilder
{
	public IntegrationEventRelayBuilder(IChatterBuilder chatterBuilder, EventMappingConfiguration configuration)
	{
		ChatterBuilder = chatterBuilder ?? throw new ArgumentNullException(nameof(chatterBuilder));
		Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	public IChatterBuilder ChatterBuilder { get; }
	public IServiceCollection Services => ChatterBuilder.Services;
	public EventMappingConfiguration Configuration { get; }
}
