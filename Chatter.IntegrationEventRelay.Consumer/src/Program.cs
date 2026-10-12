using Chatter.IntegrationEventRelay.Consumer;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddChatterCqrs(hostContext.Configuration)
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
            });
        services.AddSingleton<InMemoryConsumerCache>();
    }).Build();

var cache = host.Services.GetRequiredService<InMemoryConsumerCache>();

await host.RunAsync();

Console.WriteLine($"{cache?.Count} Integration Events Consumed: ");
Console.WriteLine(cache);

