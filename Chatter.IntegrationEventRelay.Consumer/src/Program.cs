using Chatter.IntegrationEventRelay.Consumer;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddChatterCqrs(hostContext.Configuration)
            .AddMessageBrokers(b => b.AddRecoveryOptions(r => r.UseExponentialDelayRecovery(5)))
            .AddAzureServiceBus(b => b.UseAadTokenProviderWithSecret
            (
                hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:ClientId"),
                hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:ClientSecret"),
                hostContext.Configuration.GetValue<string>("Chatter:Infrastructure:AzureServiceBus:Auth:Authority"))
            );
        services.AddSingleton<InMemoryConsumerCache>();
    }).Build();

var cache = host.Services.GetRequiredService<InMemoryConsumerCache>();

await host.RunAsync();

Console.WriteLine($"{cache?.Count} Integration Events Consumed: ");
Console.WriteLine(cache);

