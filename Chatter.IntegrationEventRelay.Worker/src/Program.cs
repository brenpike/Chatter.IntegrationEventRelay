using Chatter.IntegrationEventRelay.Cdc.Extensions;
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.ServiceBroker.Extensions;
using Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.IntegrationEvents;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddChatterCqrs(hostContext.Configuration, typeof(Event2CreatedEvent))
        .AddMessageBrokers(b => b.AddRecoveryOptions(r => r.UseExponentialDelayRecovery(5)))
        .AddAzureServiceBus(b =>
        {
            // Use Entra ID (AAD) auth only when an app registration is configured. Otherwise the connection
            // string must carry a shared access key, e.g. the Service Bus emulator's.
            var clientId = hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:ClientId");
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                b.UseAadTokenProviderWithSecret
                (
                    clientId,
                    hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:ClientSecret"),
                    hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:Authority")
                );
            }
        })
        // Event1 is captured with Change Data Capture and Event2 with SQL Service Broker; each mapping's 'Source' picks one
        .AddIntegrationEventRelay()
            .UseCdcChangeSource()
            .UseServiceBrokerChangeSource();
    }).Build();

var env = host.Services.GetRequiredService<IHostEnvironment>();
if (env.IsDevelopment())
{
    // Enables CDC on Event1 and creates the relay's tables (needs SQL Server Agent), then installs Event2's trigger and queues.
    // docker-compose runs the equivalent scripts in .scripts instead.
    await host.Services.UseCdcSqlMigrationsAsync();
    await host.Services.UseChangeFeedSqlMigrationsAsync();
}

await host.RunAsync();
