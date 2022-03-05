using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.IntegrationEvents;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddChatterCqrs(hostContext.Configuration, typeof(Event2CreatedEvent))
        .AddMessageBrokers(b => b.AddRecoveryOptions(r => r.UseExponentialDelayRecovery(5)))
        .AddAzureServiceBus(b => b.UseAadTokenProviderWithSecret
        (
            hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:ClientId"),
            hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:ClientSecret"),
            hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:Authority"))
        )
        .AddIntegrationEventRelay();
    }).Build();

var env = host.Services.GetRequiredService<IHostEnvironment>();
if (env.IsDevelopment())
    host.Services.UseChangeFeedSqlMigrations();

await host.RunAsync();
