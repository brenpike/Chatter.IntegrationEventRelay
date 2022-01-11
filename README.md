
![ci](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/ci.yml/badge.svg)
![cd](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/cd.yml/badge.svg)

Add Config/Env vars

Add sql.env to .devops folder

SA_PASSWORD="itsAbadP@SSW0RD"
ACCEPT_EULA=Y

Add worker.env to .devops folder

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

appsettings.json / appsettings.<env>.json / secrets.json

``` json
{
  "Chatter": {
    "Infrastructure": {
      "AzureServiceBus": {
        "ConnectionString": "Endpoint=sb://<insert service bus name>.servicebus.windows.net/",
        "Auth": {
          "ClientSecret": <insert client secret>,
          "ClientId": <insert client id>,
          "Authority": "https://login.microsoftonline.com/<tenant id>/"
        }
      }
    }
  },
  "IntegrationEventRelay": {
    "ConnectionString": "Data Source=DESKTOP-6D5GE0I\\SQLEXPRESS;Database=DomainEventStore;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False;ApplicationIntent=ReadWrite;MultiSubnetFailover=False",
    "Mappings": [
      {
        "SourceEventTypeName": "Event1ChangedEvent",
        "IntegrationEventTypeName": "IntegrationEventRelay.Aggregates.Fake.IntegrationEvents.Event1RestoredEvent",
        "SourceChangeType": "Update",
        "DatabaseName": "DomainEventStore",
        "TableName": "Event1"
      },
      {
        "SourceEventTypeName": "Event1ChangedEvent",
        "IntegrationEventTypeName": "IntegrationEventRelay.Aggregates.Fake.IntegrationEvents.Event1DeletedEvent",
        "SourceChangeType": "Delete",
        "DatabaseName": "DomainEventStore",
        "TableName": "Event1",
        "MessagingInfrastructureType": "Chatter.Infrastructure.AzureServiceBus",
        "InfrastructureMessageName": "event-1-deleted"
      },
      {
        "SourceEventTypeName": "Event1ChangedEvent",
        "IntegrationEventTypeName": "IntegrationEventRelay.Aggregates.Fake.IntegrationEvents.Event1DeletedEvent",
        "SourceChangeType": "Update",
        "DatabaseName": "DomainEventStore",
        "TableName": "Event1"
      },
      {
        "SourceEventTypeName": "IntegrationEventRelay.Aggregates.Fake.SourceEvents.Event2ChangedEvent",
        "IntegrationEventTypeName": "IntegrationEventRelay.Aggregates.Fake.IntegrationEvents.Event2CreatedEvent",
        "SourceChangeType": "Insert",
        "DatabaseName": "DomainEventStore",
        "TableName": "Event2"
      }
    ]
  }
}
```


docker-compose build
docker-compose up

