using Chatter.CQRS.DependencyInjection;
using Chatter.IntegrationEventRelay.Core.Builder;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatter.IntegrationEventRelay.ServiceBroker.Tests;

public sealed class FakeRelayBuilder : IIntegrationEventRelayBuilder
{
    public FakeRelayBuilder(IChatterBuilder chatterBuilder, EventMappingConfiguration configuration)
    {
        ChatterBuilder = chatterBuilder;
        Configuration = configuration;
    }

    public IChatterBuilder ChatterBuilder { get; }
    public IServiceCollection Services => ChatterBuilder.Services;
    public EventMappingConfiguration Configuration { get; }
}
