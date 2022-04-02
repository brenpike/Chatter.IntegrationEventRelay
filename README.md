![ci](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/cicd.yml/badge.svg)

[context-landscape-diagram]: https://github.com/brenpike/Chatter.IntegrationEventRelay/raw/main/.docs/assets/context-landscape-diagram.png "Context Landscape Diagram"

- [Integration Event Relay](#integration-event-relay)
  - [About this repo](#about-this-repo)
    - [Chatter.IntegrationEventRelay.Core](#chatterintegrationeventrelaycore)
    - [Chatter.IntegrationEventRelay.Worker](#chatterintegrationeventrelayworker)
    - [Chatter.IntegrationEventRelay.Consumer](#chatterintegrationeventrelayconsumer)
  - [Running this repo locally](#running-this-repo-locally)
    - [Setup Azure Service Bus](#setup-azure-service-bus)
      - [Create namespace, topics and subscriptions](#create-namespace-topics-and-subscriptions)
      - [Auth](#auth)
    - [Setting up Docker environment](#setting-up-docker-environment)
      - [sql.env](#sqlenv)
      - [worker.env](#workerenv)
      - [consumer.env](#consumerenv)
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

## Running this repo locally

### Setup Azure Service Bus

**note: Azure Service Bus is being used as it is one of the only message broker infrastructure implementations for [Chatter](https://github.com/brenpike/Chatter) at this time. Message broker infrastructure is currently being implemented for RabbitMQ, which is much more Docker friendly and will greatly simplify execution of this solution without the following setup*

#### Create namespace, topics and subscriptions

As this solution leverages [Azure Service Bus](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview) as message broker infrastructure, you must [create your own Service Bus namepsace](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions) prior to executing `docker-compose up`. Once the namespace has been created, you'll need to create the Azure Service Bus Topics that will be published as integration events by the Worker Service as well as the Topic Subscriptions that the Consumer Service will subscribe to so that it can receive any integration events that have been published to the ASB namespace. The Topic/Subscription pairs to be created are as follows (in a real situation, it's best to leverage namespacing of your Topics and much better/more descriptive subscription names):

- Topic: event-1-deleted, Subscription: sub1
- Topic: event-1-restored, Subscription: sub1
- Topic: event-2-created, Subscription: sub1

#### Auth

After these are created, you'll need to consider consider authorization and authentication with your service bus namespace. This sample Worker Service leverages the [Chatter.MessageBrokers.AzureServiceBus.Auth](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus.Auth) library, which provides multiple options, such as, client secret and certificate auth but also more development environment friendly choices such as [DefaultAzureCredential](https://docs.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet). Alternatively, if you use a service bus namespace connection string containing a valid access token (i.e., "Endpoint=sb://[insert service bus name].servicebus.windows.net/;SharedAccessKeyName=[key name];SharedAccessKey=[access key]"), this auth method will be prioritized above the supplied secret and DefaultAzureCredentials (*note: it's strongly advised to go this route for local development only and to leverage [user secrets](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=windows).

### Setting up Docker environment

To spin-up the docker environment, environment variables are required. The easiest way to approach this is by creating three environment files; one for SQL Server, one for the Worker Service and one for the Consumer Service. Once created, these environment files should be added to the .devops folder at the root of the repo.  From the root of the repo, `docker-compose build` and then `docker-compose up` to start the environment. Sample environment files:

#### sql.env

```text
SA_PASSWORD="itsAbadP@SSW0RD"
ACCEPT_EULA=Y
```

#### worker.env

```text
Logging__LogLevel__Default=Trace
DOTNET_ENVIRONMENT=Local
Chatter__Infrastructure__AzureServiceBus__ConnectionString="Endpoint=sb://<insert service bus name>.servicebus.windows.net/"
Chatter__Infrastructure__AzureServiceBus__Auth__ClientSecret=<insert client secret>
Chatter__Infrastructure__AzureServiceBus__Auth__ClientId=<insert client id>
Chatter__Infrastructure__AzureServiceBus__Auth__Authority="https://login.microsoftonline.com/<tenant id>/"
IntegrationEventRelay__ConnectionString=Server=db;Database=FakeDb;Trusted_Connection=false;User ID=sa;Password=itsAbadP@SSW0RD
IntegrationEventRelay__Mappings__0__SourceEventTypeName=Event1ChangedEvent
IntegrationEventRelay__Mappings__0__IntegrationEventTypeName=Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents.Event1RestoredEvent
IntegrationEventRelay__Mappings__0__SourceChangeType=Update
IntegrationEventRelay__Mappings__0__DatabaseName=FakeDb
IntegrationEventRelay__Mappings__0__TableName=Event1
IntegrationEventRelay__Mappings__1__SourceEventTypeName=Event1ChangedEvent
IntegrationEventRelay__Mappings__1__IntegrationEventTypeName=Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents.Event1DeletedEvent
IntegrationEventRelay__Mappings__1__SourceChangeType=Delete
IntegrationEventRelay__Mappings__1__DatabaseName=FakeDb
IntegrationEventRelay__Mappings__1__TableName=Event1
IntegrationEventRelay__Mappings__1__MessagingInfrastructureType=Chatter.Infrastructure.AzureServiceBus
IntegrationEventRelay__Mappings__1__InfrastructureMessageName=event-1-deleted
IntegrationEventRelay__Mappings__2__SourceEventTypeName=Event1ChangedEvent
IntegrationEventRelay__Mappings__2__IntegrationEventTypeName=Chatter.IntegrationEventRelay.Worker.Aggregates.Event1.IntegrationEvents.Event1DeletedEvent
IntegrationEventRelay__Mappings__2__SourceChangeType=Update
IntegrationEventRelay__Mappings__2__DatabaseName=FakeDb
IntegrationEventRelay__Mappings__2__TableName=Event1
IntegrationEventRelay__Mappings__3__SourceEventTypeName=Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.SourceEvents.Event2ChangedEvent
IntegrationEventRelay__Mappings__3__IntegrationEventTypeName=Chatter.IntegrationEventRelay.Worker.Aggregates.Event2.IntegrationEvents.Event2CreatedEvent
IntegrationEventRelay__Mappings__3__SourceChangeType=Insert
IntegrationEventRelay__Mappings__3__DatabaseName=FakeDb
IntegrationEventRelay__Mappings__3__TableName=Event2
```

#### consumer.env

```text
Logging__LogLevel__Default=Trace
DOTNET_ENVIRONMENT=Local
Chatter__Infrastructure__AzureServiceBus__ConnectionString="Endpoint=sb://<insert service bus name>.servicebus.windows.net/"
Chatter__Infrastructure__AzureServiceBus__Auth__ClientSecret=<insert client secret>
Chatter__Infrastructure__AzureServiceBus__Auth__ClientId=<insert client id>
Chatter__Infrastructure__AzureServiceBus__Auth__Authority="https://login.microsoftonline.com/<tenant id>/"
```

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
