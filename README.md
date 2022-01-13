# Integration Event Relay

The primary use case of the Integration Event Relay is to relay domain events from legacy systems to message broker infrastructure as integration events to enable an event-driven architecture. The legacy systems in question would otherwise be unable to participate in an event-driven architecture due to their as-is architecture, technical debt, or inability to integrate with message broker infrastructure.

The Integration Event Relay leverages SQL Service Broker via [Chatter.SqlTableWatcher](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlTableWatcher) to watch for changes made to database tables. The modified data is added to a SQL Service Broker queue to be consumed by a [Worker service](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Worker) where it will be mapped to an integration event and relayed to message broker infrastructure. As SQL Service Broker is core to the implementation, it is required that the legacy system leverages SQL as it's backend, it is also imparative that the SQL server infrastructure support SQL Service Broker (i.e., most PaaS offerings do not).

---

## About this repo

This repo contains three projects, [Chatter.IntegrationEventRelay.Worker](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Worker), [Chatter.IntegrationEventRelay.Consumer](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Consumer) and [Chatter.IntegrationEventRelay.Core](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Core). It is a fully functional implementation that leverages Docker to spin-up a SQL Server, 2 Worker Services and 2 Consumer Services. The SQL Server schema is created and seeded with data on startup; subsequently, 100 unique database operations are executed against the database tables which emulate changes to domain entities/aggregates. These changes are handled by the Worker Services as domain events, where mapping logic is executed. If mapping logic is successful, the domain events are converted to the integration event defined by configuration and relayed/published to the message broker infrastructure. The integration events are handled by the Consumer Services (as they are subscribed to all integration events) and details of the messages are displayed to console to show successful propogation of the database changes to the consumers. The number of integration events handled by each consumer is cached in memory and displayed upon shut down - each consumer should handled 50 integration events each.

### Chatter.IntegrationEventRelay.Core

As the name suggests, this is the core of the implementation. It leverages [Chatter.SqlTableWatcher](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlTableWatcher) and contains interfaces, services, configuration definitions and extension methods required to quickly and easily build one or more Worker Services. This project is available via nuget as [Chatter.IntegrationEventRelay](https://www.nuget.org/packages/Chatter.IntegrationEventRelay/).

### Chatter.IntegrationEventRelay.Worker

This Worker Service implementation is a sample showing how [Chatter.IntegrationEventRelay.Core](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Core) can be used to build your own Worker Service. This Worker Service leverages [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus) as its message broker infrastructure, but any other infrastructure implementation can be leveraged by changing the [app wire-up](https://github.com/brenpike/Chatter.IntegrationEventRelay/blob/main/Chatter.IntegrationEventRelay.Worker/src/Program.cs). Its primary function is to listen for changes to configured database tables and relay them as integration events to the message broker infrastructure.

### Chatter.IntegrationEventRelay.Consumer

The Consumer Service has been kept simple. It has [IEvent](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/Events/IEvent.cs) implementations matching the integration events published by the Worker Service as well as [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) implementations. The Consumer Service leverages [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus) as its message broker infrastructure and subscribes to the integration events published by the Worker Services. The [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) handles the integration events and displays information to console. The handled messages are cached in memory and all handled messages are displayed on shutdown.

---

## Running this repo locally

### Azure Service Bus

**note: Azure Service Bus is being used as it is one of the only message broker infrastructure implementations for [Chatter](https://github.com/brenpike/Chatter) at this time. Message broker infrastructure is currently being implemented for RabbitMQ, which is much more Docker friendly and will greatly simplify execution of this solution without the following setup*

As this solution leverages [Azure Service Bus](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview) as message broker infrastructure, you must [create your own Service Bus namepsace](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-how-to-use-topics-subscriptions) prior to executing `docker-compose up`. Once the namespace has been created, you'll need to create the Azure Service Bus Topics that will be published as integration events by the Worker Service as well as the Topic Subscriptions that the Consumer Service will subscribe to so that it can receive any integration events that have been published to the ASB namespace. The Topic/Subscription pairs to be created are as follows (in a real situation, it's best to leverage namespacing of your Topics and much better/more descriptive subscription names):

- Topic: event-1-deleted, Subscription: sub1
- Topic: event-1-restored, Subscription: sub1
- Topic: event-2-created, Subscription: sub1

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