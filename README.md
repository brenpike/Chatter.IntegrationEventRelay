![ci](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/cicd.yml/badge.svg)

[context-landscape-diagram]: https://github.com/brenpike/Chatter.IntegrationEventRelay/raw/main/.docs/assets/context-landscape-diagram.png "Context Landscape Diagram"

- [Integration Event Relay](#integration-event-relay)
  - [About this repo](#about-this-repo)
    - [Chatter.IntegrationEventRelay.Core](#chatterintegrationeventrelaycore)
    - [Chatter.IntegrationEventRelay.Worker](#chatterintegrationeventrelayworker)
    - [Chatter.IntegrationEventRelay.Consumer](#chatterintegrationeventrelayconsumer)
  - [Upgrading to 0.3.0](#upgrading-to-030)
  - [Running this repo locally](#running-this-repo-locally)
    - [Service Bus emulator](#service-bus-emulator)
    - [Using a real Azure Service Bus namespace](#using-a-real-azure-service-bus-namespace)
  - [Building a Worker Service](#building-a-worker-service)
    - [Create your dotnet Worker Service](#create-your-dotnet-worker-service)
    - [Add required dependencies](#add-required-dependencies)
      - [Add Chatter.IntegrationEventRelay](#add-chatterintegrationeventrelay)
      - [Add a Chatter Message Broker Infrastructure implementation](#add-a-chatter-message-broker-infrastructure-implementation)
      - [Add user secrets](#add-user-secrets)
    - [Implement `Program.cs`](#implement-programcs)
    - [Create and seed SQL database](#create-and-seed-sql-database)
      - [Create OrdersDB database](#create-ordersdb-database)
      - [Create Orders table](#create-orders-table)
      - [Seed data](#seed-data)
    - [Create Source Event Dto](#create-source-event-dto)
    - [Create Integration Event Dto](#create-integration-event-dto)
    - [Add mapping to `secrets.json` or `appsettings.json`](#add-mapping-to-secretsjson-or-appsettingsjson)
    - [Create Concrete `IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIngegrationEvent>` implementation](#create-concrete-imapsourceupdatetointegrationeventtsourceevent-tingegrationevent-implementation)
    - [Try it out](#try-it-out)

# Integration Event Relay

The primary use case of the Integration Event Relay is to relay domain events from legacy systems to message broker infrastructure as integration events to enable an event-driven architecture. The legacy systems in question would otherwise be unable to participate in an event-driven architecture due to their as-is architecture, technical debt, or inability to integrate with message broker infrastructure.

The Integration Event Relay leverages SQL Service Broker via [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed) to watch for changes made to database tables. The modified data is added to a SQL Service Broker queue to be consumed by a [Worker service](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Worker) where it will be mapped to an integration event and relayed to message broker infrastructure. As SQL Service Broker is core to the implementation, it is required that the legacy system leverages SQL as it's backend, it is also imparative that the SQL server infrastructure support SQL Service Broker (i.e., most PaaS offerings do not).

![Context Landspace Diagram][context-landscape-diagram]

## About this repo

This repo contains three projects, [Chatter.IntegrationEventRelay.Worker](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Worker), [Chatter.IntegrationEventRelay.Consumer](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Consumer) and [Chatter.IntegrationEventRelay.Core](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Core). It is a fully functional implementation that leverages Docker to spin-up a SQL Server, 2 Worker Services and 2 Consumer Services. The SQL Server schema is created and seeded with data on startup; subsequently, 100 unique database operations are executed against the database tables which emulate changes to domain entities/aggregates. These changes are handled by the Worker Services as domain events, where mapping logic is executed. If mapping logic is successful, the domain events are converted to the integration event defined by configuration and relayed/published to the message broker infrastructure. The integration events are handled by the Consumer Services (as they are subscribed to all integration events) and details of the messages are displayed to console to show successful propogation of the database changes to the consumers. The number of integration events handled by each consumer is cached in memory and displayed upon shut down - each consumer should handle 50 integration events each.

### Chatter.IntegrationEventRelay.Core

As the name suggests, this is the core of the implementation. It leverages [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed) and contains interfaces, services, configuration definitions and extension methods required to quickly and easily build one or more Worker Services. This project is available via nuget as [Chatter.IntegrationEventRelay](https://www.nuget.org/packages/Chatter.IntegrationEventRelay/).

### Chatter.IntegrationEventRelay.Worker

This Worker Service implementation is a sample showing how [Chatter.IntegrationEventRelay.Core](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Core) can be used to build your own Worker Service. This Worker Service leverages [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus) as its message broker infrastructure, but any other infrastructure implementation can be leveraged by changing the [app wire-up](https://github.com/brenpike/Chatter.IntegrationEventRelay/blob/main/Chatter.IntegrationEventRelay.Worker/src/Program.cs). Its primary function is to listen for changes to configured database tables and relay them as integration events to the message broker infrastructure.

### Chatter.IntegrationEventRelay.Consumer

The Consumer Service has been kept simple. It has [IEvent](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/Events/IEvent.cs) implementations matching the integration events published by the Worker Service as well as [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) implementations. The Consumer Service leverages [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus) as its message broker infrastructure and subscribes to the integration events published by the Worker Services. The [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) handles the integration events and displays information to console. The handled messages are cached in memory and all handled messages are displayed on shutdown.

## Upgrading to 0.3.0

- Targets .NET 10 (`net10.0`); .NET 6 is no longer supported.
- Uses the latest Chatter packages, which connect to SQL Server with `Microsoft.Data.SqlClient` instead of `System.Data.SqlClient`. `Microsoft.Data.SqlClient` encrypts connections and validates the server certificate by default. If your SQL Server does not present a trusted certificate (for example a local or containerized SQL Server with a self-signed certificate), connections that worked before will now fail. Either provision a trusted certificate, or add `TrustServerCertificate=True` to the connection string (development only; it disables certificate validation).
- `UseChangeFeedSqlMigrations()` is obsolete; use `await UseChangeFeedSqlMigrationsAsync()`.

## Running this repo locally

The demo runs entirely in Docker: SQL Server, the [Azure Service Bus emulator](https://learn.microsoft.com/en-us/azure/service-bus-messaging/overview-emulator), two Worker Services and two Consumer Services. No Azure subscription or configuration is needed.

From the root of the repo:

```bash
docker compose up --build
```

On startup the database is created and seeded, and 100 database operations are executed against it. The Workers relay them as integration events through the emulator, and each Consumer logs every event it handles. Stop with `Ctrl+C`, then `docker compose down -v` to remove the containers.

To run the same check CI does (start the stack and wait until all 100 integration events have been consumed):

```bash
./.devops/e2e.sh
docker compose down -v
```

*Note: running the demo starts SQL Server and the Service Bus emulator containers with `ACCEPT_EULA=Y`, which accepts the SQL Server Developer Edition and [Service Bus emulator](https://github.com/Azure/azure-service-bus-emulator-installer/blob/main/EMULATOR_EULA.txt) license terms.*

### Service Bus emulator

The emulator's topics and subscriptions are declared in [`.devops/servicebus-emulator/Config.json`](.devops/servicebus-emulator/Config.json), because the emulator cannot create entities at runtime:

- Topic: event-1-deleted, Subscription: sub1
- Topic: event-1-restored, Subscription: sub1
- Topic: event-2-created, Subscription: sub1

The emulator uses the demo's SQL Server container as its backing store and keeps no state between runs. The Workers and Consumers connect with the emulator's shared access key connection string, which `docker-compose.yml` sets by default.

### Using a real Azure Service Bus namespace

To relay to a real namespace instead, create the topics and subscriptions above in your namespace, then create a `.env` file next to `docker-compose.yml` (it is git-ignored):

```text
# Either a connection string with a shared access key...
ASB_CONNECTION_STRING=Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=<key name>;SharedAccessKey=<key>
# ...or the namespace endpoint plus an Entra ID (AAD) app registration
# ASB_CONNECTION_STRING=Endpoint=sb://<your-namespace>.servicebus.windows.net/
# ASB_CLIENT_ID=<app registration client id>
# ASB_CLIENT_SECRET=<app registration secret>
# ASB_AUTHORITY=https://login.microsoftonline.com/<tenant id>/
```

Entra ID auth is used only when `ASB_CLIENT_ID` is set; a connection string with a shared access key always takes priority. The samples use the [Chatter.MessageBrokers.AzureServiceBus.Auth](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus.Auth) library, which also supports certificate, managed identity and interactive auth. Keep secrets out of source control, for example with [user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) when running outside Docker.

The SQL Server `sa` password defaults to the demo value; override it with `SA_PASSWORD` in the same `.env` file.

## Building a Worker Service

### Create your dotnet Worker Service

To build your own Worker service from scratch, open new terminal window of choice (cmd, PS, shell, etc.) and execute the following commands:

`mkdir MyNewIntegrationEventRelay`
`cd .\MyNewIntegrationEventRelay\`
`dotnet new worker`
`.\MyNewIntegrationEventRelay.csproj`

You can remove the out-of-the-box worker class as the Chatter framework will create Background Services to monitor for database changes:
`rm Worker.cs`

### Add required dependencies

#### Add Chatter.IntegrationEventRelay

The Chatter.IntegrationEventRelay nuget package, will add the functionality of [Chatter.IntegrationEventRelay.Core](#chatterintegrationeventrelaycore) to the newly created Worker service:
`dotnet add package Chatter.IntegrationEventRelay`

#### Add a Chatter Message Broker Infrastructure implementation

For this example, we'll use Azure Service Bus. Follow the instructions [here](#create-namespace-topics-and-subscriptions) to configure your Azure Service Bus namespace.
`dotnet add package Chatter.MessageBrokers.AzureServiceBus`

Optionally, add the Chatter Azure Service Bus Auth library which provides extension methods to [easily auth with Azure Service Bus](#auth).
`dotnet add package Chatter.MessageBrokers.AzureServiceBus.Auth`

#### Add user secrets

If you'd like to leverage user secrets, add the following package, otherwise use `appsettings.json`. User secrets are prefered for local development to ensure your secrets remain...secret.
`dotnet add package Microsoft.Extensions.Configuration.UserSecrets`

### Implement `Program.cs`

Modify `Program.cs` to look like the following. Configure Chatter using the builder classes available as optional parameters of the various Chatter extension methods:

```csharp
using Chatter.IntegrationEventRelay.Core.Extensions;

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
        )
        //Wire up all implementations of IMapSourceToIntegrationEvent<TSourceEvent, TIntegrationEvent> defined in configuration
        .AddIntegrationEventRelay(); 
    }).Build();

var env = host.Services.GetRequiredService<IHostEnvironment>();
if (env.IsDevelopment())
    //Creates all database dependencies, including Install/Uninstall stored procs, triggers and SQL Service Broker queues. Should only be used in a development environment. Created DB objects should be deployed by other means (i.e., dacpac).
    host.Services.UseChangeFeedSqlMigrations(); 

await host.RunAsync();
```

### Create and seed SQL database

Connect to `(localdb)\MSSQLLocalDB` and execute the following:

#### Create OrdersDB database

```sql
USE [master]
GO

CREATE DATABASE [OrdersDB]
```

#### Create Orders table

```sql
USE [OrdersDB]
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Orders](
	[Id] [uniqueidentifier] NOT NULL,
	[CustomerId] [uniqueidentifier] NOT NULL,
	[ProductId] [uniqueidentifier] NOT NULL,
	[Quantity] [smallint] NOT NULL,
	[OrderStatus] [varchar](25) NULL,
	[OrderPlacedOn] [smalldatetime] NOT NULL,
 CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO
```

#### Seed data

```sql
USE [OrdersDB]
GO

INSERT INTO [dbo].[Orders]
           ([Id]
           ,[CustomerId]
           ,[ProductId]
           ,[Quantity]
           ,[OrderStatus]
           ,[OrderPlacedOn])
     VALUES
           ('52CBE9FF-2496-4F46-9331-7F122AA3D973'
           ,'99FD8C91-F197-48BA-995C-17BD89D4394D'
           ,'170310D2-6EF8-47B0-9B4C-62BA088EA7BE'
           ,10
           ,'Pending'
           ,GETDATE())
GO
```

### Create Source Event Dto

A Dto representing a row of the `Orders` table must be created. When changes are made to `Orders` these changes are queued to Sql Service Broker and consumed by a background service that is wired up on start-up via [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed).

```csharp
using Chatter.IntegrationEventRelay.Core;

namespace MyNewIntegrationEventRelay
{
    public class OrderChangedEvent : ISourceEvent //Note the required implementation of the ISourceEvent interface
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public string? OrderStatus { get; set; }
        public DateTime OrderPlacedOn { get; set; }
    }
}

```

### Create Integration Event Dto

A Dto representing the integration event (`TIntegrationEvent`) that will be relayed to the message broker infrastructure. This Dto will be used as the message body.

Create the "order-shipped-event" topic with a single subscription on the previously created Azure Service Bus namespace. The subscription will allow you to see the relayed messages via the Azure Portal. Instructions [here](#create-namespace-topics-and-subscriptions).

```csharp
using Chatter.CQRS.Events;
using Chatter.MessageBrokers;

namespace MyNewIntegrationEventRelay
{
    //The BrokeredMessageAttribute lets the Chatter framework which topic to publish this integration event to
    [BrokeredMessage("order-shipped-event")]
    public class OrderShippedEvent : IEvent //Note the required implementation of the IEvent interface
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid ProductId { get; set; }
        public DateTime OrderPlacedOn { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
```

### Add mapping to `secrets.json` or `appsettings.json`

Modify `secrets.json` or `appsettings.json` to look as follows. The AzureServiceBus config section must be modified to match values for your Service Bus namespace. See [Service Bus Auth section](#auth) for more information.

Note the "Mappings" section. The lone entry maps the[source event](#create-source-event-dto) (`TSourceEvent`) generated by an `Update` operation made to the `Orders` table to the [integration event](#create-integration-event-dto) (`TIntegrationEvent`).

At minimum, each [EventMappingConfigurationItem](https://github.com/brenpike/Chatter.IntegrationEventRelay/blob/main/Chatter.IntegrationEventRelay.Core/src/Configuration/EventMappingConfigurationItem.cs) must have a value for `SourceEventTypeName`, `IntegrationEventTypeName`, `TableName` and `SourceChangeType` (see class for detailed property definitions).

This mapping is defined by the concrete `IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIngegrationEvent>` implemented in the [following section](#create-concrete-imapsourceupdatetointegrationeventtsourceevent-tingegrationevent-implementation).

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Trace",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Chatter": {
    "Infrastructure": {
      "AzureServiceBus": {
        "ConnectionString": "Endpoint=sb://<your-sb-namespace>.servicebus.windows.net/",
        "Auth": {
          "ClientSecret": "<your-app-registration-secret>",
          "ClientId": "<your-app-registration-id",
          "Authority": "https://login.microsoftonline.com/<your-tenant/directory-id>/"
        }
      }
    }
  },
  "IntegrationEventRelay": {
    "ConnectionString": "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=OrdersDB;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False;ApplicationIntent=ReadWrite;MultiSubnetFailover=False",
    "Mappings": [
      {
        "SourceEventTypeName": "MyNewIntegrationEventRelay.OrderChangedEvent",
        "IntegrationEventTypeName": "MyNewIntegrationEventRelay.OrderShippedEvent",
        "SourceChangeType": "Update",
        "TableName": "Orders"
      }
    ]
  }
}

```

### Create Concrete `IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIngegrationEvent>` implementation

A mapper is required to define how `TSourceEvent` should be mapped to `TIntegrationEvent` before it is relayed to message broker infrasrtucture. Depending on the [configured](#add-mapping-to-secretsjson-or-appsettingsjson) `SourceChangeType`, the appropriate mapping interface must be implemented. The implementation of `MapAsync` should return a new instance of `TIntegrationEvent` if the `TSourceEvent` that was received meets the optional criteria and return `null` otherwise. In this concrete implementation the criteria is `if (mappingData.OldValue?.OrderStatus != "Shipped" && mappingData.NewValue?.OrderStatus == "Shipped")`. If the criteria is not met and `null` is returned, no integration event will be relayed.

- For `SourceChangeType` = "Delete", `IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent>` must be created
- For `SourceChangeType` = "Update", `IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent>` must be created
- For `SourceChangeType` = "Insert", `IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent>` must be created

```csharp
using Chatter.IntegrationEventRelay.Core.Mapping;

namespace MyNewIntegrationEventRelay
{
    public class OrderShippedMapper : IMapSourceUpdateToIntegrationEvent<OrderChangedEvent, OrderShippedEvent>
    {
        public Task<OrderShippedEvent> MapAsync(MappingData<OrderChangedEvent> mappingData, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig)
        {
            OrderShippedEvent orderShippedEvent = null;

            //mappingData.OldValue will only have a value on an Update operation
            if (mappingData.OldValue?.OrderStatus != "Shipped" && mappingData.NewValue?.OrderStatus == "Shipped")
            {
                orderShippedEvent = new OrderShippedEvent()
                {
                    Id = mappingData.NewValue.Id,
                    CustomerId = mappingData.NewValue.CustomerId,
                    ProductId = mappingData.NewValue.ProductId,
                    OccurredAt = DateTime.UtcNow,
                    OrderPlacedOn = mappingData.NewValue.OrderPlacedOn
                };
            }

            return Task.FromResult(orderShippedEvent);
        }
    }
}
```

### Try it out

Put a break point on the return statement of the `OrderShippedMapper` that was [just created](#create-concrete-imapsourceupdatetointegrationeventtsourceevent-tingegrationevent-implementation). Executing the following statement will update the status of the order that was [previously seeded](#seed-data) which will hit your breakpoint. As the condition was met, a non-null integration event will be returned and will be relayed to the `order-shipped-event` topic. Inspect the subscription you created earlier to see the message.

```SQL
USE [OrdersDB]
GO

UPDATE [dbo].[Orders]
   SET [OrderStatus] = 'Shipped'
 WHERE Id = '52CBE9FF-2496-4F46-9331-7F122AA3D973'
GO
```
