![ci](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/cicd.yml/badge.svg)

[context-landscape-diagram]: https://github.com/brenpike/Chatter.IntegrationEventRelay/raw/main/.docs/assets/context-landscape-diagram.png "Context Landscape Diagram"

- [Integration Event Relay](#integration-event-relay)
  - [About this repo](#about-this-repo)
    - [Chatter.IntegrationEventRelay.Core](#chatterintegrationeventrelaycore)
    - [Chatter.IntegrationEventRelay.ServiceBroker](#chatterintegrationeventrelayservicebroker)
    - [Chatter.IntegrationEventRelay.Cdc](#chatterintegrationeventrelaycdc)
    - [Chatter.IntegrationEventRelay.Worker](#chatterintegrationeventrelayworker)
    - [Chatter.IntegrationEventRelay.Consumer](#chatterintegrationeventrelayconsumer)
  - [Choosing a change source](#choosing-a-change-source)
    - [Delivery semantics](#delivery-semantics)
  - [Using the CDC change source](#using-the-cdc-change-source)
    - [Enable CDC](#enable-cdc)
    - [Permissions](#permissions)
    - [Configure the relay](#configure-the-relay)
    - [Error policy and parked changes](#error-policy-and-parked-changes)
    - [Metrics and health check](#metrics-and-health-check)
    - [Limitations](#limitations)
  - [Switching a table from SQL Service Broker to CDC](#switching-a-table-from-sql-service-broker-to-cdc)
  - [Upgrading to 0.4.0](#upgrading-to-040)
    - [Migrating mappers](#migrating-mappers)
  - [Upgrading to 0.3.0](#upgrading-to-030)
  - [Running this repo locally](#running-this-repo-locally)
    - [Change sources in the demo](#change-sources-in-the-demo)
    - [Service Bus emulator](#service-bus-emulator)
    - [Using a real Azure Service Bus namespace](#using-a-real-azure-service-bus-namespace)
  - [Building a Worker Service](#building-a-worker-service)
    - [Create your dotnet Worker Service](#create-your-dotnet-worker-service)
    - [Add required dependencies](#add-required-dependencies)
      - [Add Chatter.IntegrationEventRelay](#add-chatterintegrationeventrelay)
      - [Add a change source](#add-a-change-source)
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

The Integration Event Relay watches database tables for changes with a *change source*, maps each change to an integration event in a [Worker service](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Worker) and relays it to message broker infrastructure. The legacy system must use SQL Server as its backend. Two change sources are available, and each mapping picks one (see [Choosing a change source](#choosing-a-change-source)):

- **SQL Service Broker**, via [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed): a trigger on each watched table adds the modified rows to a SQL Service Broker queue, which the Worker consumes. The SQL Server must support SQL Service Broker (most PaaS offerings don't).
- **SQL Server Change Data Capture (CDC)**: the Worker polls the change tables CDC fills from the transaction log, in commit order, and resumes from a checkpoint. No trigger is installed on the table.

![Context Landspace Diagram][context-landscape-diagram]

## About this repo

This repo contains five projects, [Chatter.IntegrationEventRelay.Worker](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Worker), [Chatter.IntegrationEventRelay.Consumer](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Consumer), [Chatter.IntegrationEventRelay.Core](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Core), [Chatter.IntegrationEventRelay.ServiceBroker](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.ServiceBroker) and [Chatter.IntegrationEventRelay.Cdc](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Cdc). It is a fully functional implementation that leverages Docker to spin-up a SQL Server, 2 Worker Services and 2 Consumer Services. The SQL Server schema is created and seeded with data on startup; subsequently, 100 unique database operations are executed against the database tables which emulate changes to domain entities/aggregates. Changes to one table are captured with CDC and changes to the other with SQL Service Broker (see [Change sources in the demo](#change-sources-in-the-demo)). These changes are handled by the Worker Services as domain events, where mapping logic is executed. If mapping logic is successful, the domain events are converted to the integration event defined by configuration and relayed/published to the message broker infrastructure. The integration events are handled by the Consumer Services (as they are subscribed to all integration events) and details of the messages are displayed to console to show successful propogation of the database changes to the consumers. The number of integration events handled by each consumer is cached in memory and displayed upon shut down - each consumer should handle 50 integration events each.

### Chatter.IntegrationEventRelay.Core

As the name suggests, this is the core of the implementation. It contains the mapping configuration, mapper interfaces, the processor that maps row changes to integration events and publishes them, and the extension methods required to quickly and easily build one or more Worker Services. It doesn't capture changes itself: a change source package does (see below). This project is available via nuget as [Chatter.IntegrationEventRelay](https://www.nuget.org/packages/Chatter.IntegrationEventRelay/).

### Chatter.IntegrationEventRelay.ServiceBroker

The SQL Service Broker change source. It leverages [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed), which installs a trigger on each watched table that sends changed rows to a SQL Service Broker queue, and hands the received changes to the core processor. This project is available via nuget as Chatter.IntegrationEventRelay.ServiceBroker.

### Chatter.IntegrationEventRelay.Cdc

The SQL Server Change Data Capture (CDC) change source. It polls each capture instance's change table, hands the changes to the core processor in commit order, and saves a checkpoint in the source database once a batch is published. See [Using the CDC change source](#using-the-cdc-change-source). This project is available via nuget as Chatter.IntegrationEventRelay.Cdc.

### Chatter.IntegrationEventRelay.Worker

This Worker Service implementation is a sample showing how [Chatter.IntegrationEventRelay.Core](https://github.com/brenpike/Chatter.IntegrationEventRelay/tree/main/Chatter.IntegrationEventRelay.Core) can be used to build your own Worker Service. This Worker Service leverages [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus) as its message broker infrastructure, but any other infrastructure implementation can be leveraged by changing the [app wire-up](https://github.com/brenpike/Chatter.IntegrationEventRelay/blob/main/Chatter.IntegrationEventRelay.Worker/src/Program.cs). Its primary function is to listen for changes to configured database tables and relay them as integration events to the message broker infrastructure.

### Chatter.IntegrationEventRelay.Consumer

The Consumer Service has been kept simple. It has [IEvent](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/Events/IEvent.cs) implementations matching the integration events published by the Worker Service as well as [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) implementations. The Consumer Service leverages [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus) as its message broker infrastructure and subscribes to the integration events published by the Worker Services. The [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) handles the integration events and displays information to console. The handled messages are cached in memory and all handled messages are displayed on shutdown.

## Choosing a change source

Each mapping names its source in `Source`; a mapping without one uses SQL Service Broker with the relay's `ConnectionString`. One relay can use both sources, but each table uses only one.

Use **CDC** when:

- mappers compare old and new values or depend on the order of changes (for example soft delete followed by restore), since CDC delivers each table's changes in commit order and keeps the before-image of every update;
- you can't, or don't want to, add triggers to the legacy system's tables, or the writes must not pay for a trigger and a Service Broker send;
- the relay must resume where it stopped after downtime, from changes still in the change table.

Use **SQL Service Broker** when:

- SQL Server Agent isn't available (CDC's capture job needs it), or CDC can't be enabled on the database;
- changes must reach the relay without CDC's capture latency (the capture job scans the log every few seconds by default);
- the table is already relayed this way and works as needed.

CDC needs SQL Server Agent, `sysadmin` to enable it on the database, and `db_owner` to enable it on each table. The relay is tested against SQL Server 2019 and 2022; Azure SQL Managed Instance and Azure SQL Database also offer CDC, but the relay hasn't been tested there.

### Delivery semantics

| | SQL Service Broker | CDC |
|---|---|---|
| Capture | Trigger in the writing transaction | Capture job reads the transaction log |
| Order | Not guaranteed | Commit order per table (capture instance); no order across tables |
| Delivery | At least once (Chatter's receive transaction and recovery options) | At least once: a batch is checkpointed only after every change in it is published |
| Message id | Generated by Chatter | Derived from the source name, the change's position and the integration event type, so a replay republishes the same ids |
| Commit time (`RelayContext.CommittedAt`) | Not available | Available (see [Limitations](#limitations)) |
| Capabilities | None | `Ordered`, `Resumable`, `CommitTime` (`ParkRow` drops `Ordered`) |
| After downtime | Changes wait in the queue | Resumes from the checkpoint while the changes are still in the change table |

With CDC, a crash after publishing but before saving the checkpoint republishes the batch with the same message ids. Enable duplicate detection on the topics (Azure Service Bus) so the broker drops the replays, or make consumers idempotent.

A mapping can require capabilities with `RequiredCapabilities` (for example `"Ordered"` or `"Ordered, CommitTime"`); the relay stops at startup if the mapping's source doesn't provide them.

## Using the CDC change source

### Enable CDC

Run [`.scripts/cdc/enable-cdc.sql`](.scripts/cdc/enable-cdc.sql) in the source database for each table (set `@tableSchema` and `@tableName` at the top), and [`.scripts/cdc/create-relay-tables.sql`](.scripts/cdc/create-relay-tables.sql) once to create the relay's checkpoint and parked-change tables (set `@schema` if the relay's `Schema` isn't `Chatter`). Both scripts are safe to run again. SQL Server Agent must be running; while it is still starting, enabling CDC on the first table fails with error 14258.

In development, `await host.Services.UseCdcSqlMigrationsAsync()` does the same for every mapped table.

The capture instance's retention (`sys.sp_cdc_change_job @job_type = 'cleanup', @retention = <minutes>`, default 3 days) is how long the relay can be down without losing changes.

### Permissions

The relay's login needs:

```sql
GRANT SELECT ON dbo.Orders TO [relay_user];                           -- each captured table (every captured column)
GRANT SELECT ON SCHEMA::cdc TO [relay_user];                          -- change tables and CDC metadata
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::Chatter TO [relay_user]; -- the relay's tables
```

If the capture instance was enabled with a gating role (`@role_name`), the login must also be a member of it. Without `SELECT` on the captured table SQL Server reports no changes rather than an error; the relay detects this and stops with an error.

### Configure the relay

Add the package and chain `.UseCdcChangeSource()` after `.AddIntegrationEventRelay()` (with `.UseServiceBrokerChangeSource()` too if other mappings use SQL Service Broker):

`dotnet add package Chatter.IntegrationEventRelay.Cdc`

```csharp
.AddIntegrationEventRelay()
    .UseCdcChangeSource()
    .UseServiceBrokerChangeSource();
```

Declare a source of type `Cdc` and name it in each mapping of the table:

```json
"IntegrationEventRelay": {
  "ConnectionString": "...",
  "Schema": "Chatter",
  "Sources": {
    "orders-cdc": { "Type": "Cdc", "PollInterval": "00:00:01" }
  },
  "Mappings": [
    {
      "Source": "orders-cdc",
      "SourceEventTypeName": "MyNewIntegrationEventRelay.OrderChangedEvent",
      "IntegrationEventTypeName": "MyNewIntegrationEventRelay.OrderShippedEvent",
      "SourceChangeType": "Update",
      "TableName": "Orders",
      "TableSchema": "dbo",
      "CaptureInstance": "dbo_Orders"
    }
  ]
}
```

- `Schema` (relay-wide, default `Chatter`) is where the relay keeps its tables (`Chatter_RelayCheckpoints`, `Chatter_RelayParkedChanges`) in the source database. SqlChangeFeed's SQL Service Broker objects stay in each watched table's schema.
- A source's `ConnectionString` is optional; the relay's `ConnectionString` is used without it.
- `TableSchema` (default `dbo`) and `CaptureInstance` (default `<TableSchema>_<TableName>`) are optional, per mapping.
- Source options: `PollInterval` (1s), `MaxTransactionsPerBatch` (500), `StartPosition` (`Earliest` or `Latest`, used only when there's no checkpoint), `LeaseRetryInterval` (15s), `RetryDelay` (1s), `MaxRetryDelay` (1m), `FailuresBeforeCritical` (5), `ErrorPolicy` (`Halt` or `ParkRow`) and `MaxAttemptsBeforePark` (5). `UseCdcChangeSource(o => ...)` can also set them, including `IsTransient`.
- Source event types need a public parameterless constructor; captured columns set the properties of the same name (case-insensitive). They don't need to implement `ISourceEvent`.
- Several instances of the relay can run with the same configuration: each source's capture instance is polled by one of them at a time (a SQL Server application lock), and the others stand by. The lock and the checkpoint belong to the source name and the capture instance, so two sources that read the same capture instance poll it independently and each relay every change.
- Integration events are published outside any receive transaction, so Chatter's outbox isn't supported.

### Error policy and parked changes

Transient failures (for example the broker is unavailable or a SQL timeout) are retried with exponential backoff and never parked. For a change that keeps failing with other errors (for example a mapper exception):

- `Halt` (default): the poller stops at the change and keeps retrying it, logging critical after `FailuresBeforeCritical` attempts. Nothing after it is published, so order always holds.
- `ParkRow`: after `MaxAttemptsBeforePark` attempts the change is written to `<Schema>.Chatter_RelayParkedChanges`, and later changes to the same row are parked behind it. Other rows carry on and the checkpoint advances, so the source is then only ordered per row and doesn't provide `Ordered`. The table needs a primary key or unique index. Once the cause is fixed, request a replay; the relay republishes the row's parked changes in order and deletes them:

```sql
UPDATE Chatter.Chatter_RelayParkedChanges SET ReplayRequestedAt = SYSUTCDATETIME()
WHERE SourceName = N'orders-cdc' AND CaptureInstance = N'dbo_Orders' AND RowKey = N'<row key, e.g. [42]>';
```

To replay or skip changes, update `LastLsn` in `<Schema>.Chatter_RelayCheckpoints`, or delete the row to start again from `StartPosition`.

### Metrics and health check

Metrics are published on meter `Chatter.IntegrationEventRelay.Cdc` (`System.Diagnostics.Metrics`), tagged with `source` and `capture_instance`:

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `chatter.relay.cdc.changes` | Counter | `{change}` | Changes relayed (published or, with `ParkRow`, parked) |
| `chatter.relay.cdc.parked` | Counter | `{change}` | Changes parked |
| `chatter.relay.cdc.batch.failures` | Counter | `{batch}` | Failed attempts at a batch |
| `chatter.relay.cdc.batch.duration` | Histogram | `s` | Time to read, process and checkpoint a batch |
| `chatter.relay.cdc.lag` | Gauge | `s` | Time since the last relayed change was committed; 0 when caught up |
| `chatter.relay.cdc.parked.current` | Gauge | `{change}` | Changes currently parked |

Collect them with OpenTelemetry (`.AddMeter("Chatter.IntegrationEventRelay.Cdc")`) or `dotnet-counters`.

The health check reports this instance's pollers:

```csharp
services.AddHealthChecks().AddCdcChangeSource(maxLag: TimeSpan.FromMinutes(5));
```

It is unhealthy when a poller stopped on a gap, failed `FailuresBeforeCritical` times in a row, or lags more than `maxLag`; degraded while retrying a batch or while changes are parked. Standby instances are healthy.

### Limitations

- **Commit times and daylight saving time:** CDC records commit times in the server's local time, and the relay converts them with the server's UTC offset when it reads the batch. If the server's time zone observes daylight saving time, changes committed before a clock change but read after it are off by the change. Run SQL Server in UTC to avoid this.
- **Gaps:** if CDC cleanup removes changes the relay hasn't published (the relay was down longer than the retention period), the poller stops and the health check is unhealthy. Increase the retention, then update or delete the checkpoint to resume; the removed changes are lost.
- **Schema changes:** a capture instance keeps the columns it had when it was created. After adding columns, create a new capture instance, point the mapping's `CaptureInstance` at it, and seed its checkpoint; the relay doesn't switch capture instances by itself.
- **Partition switching:** rows moved with `ALTER TABLE ... SWITCH` aren't recorded by CDC, so they aren't relayed (the relay warns at startup).
- **Large-value columns:** in an update's before-image, CDC records `varchar(max)`, `nvarchar(max)` and `varbinary(max)` columns as NULL unless they changed, and `text`, `ntext` and `image` columns always as NULL. So those properties of `OldValue` may be null (or default).

## Switching a table from SQL Service Broker to CDC

The goal is that each change is relayed by only one of the two sources: changes up to a point by SQL Service Broker, later ones by CDC. Each source still delivers at least once (see [Delivery semantics](#delivery-semantics)). Do it in a short write pause on the table; without one, changes made during the switch are relayed by both sources, with different message ids.

1. Enable CDC on the table and create the relay's tables ([Enable CDC](#enable-cdc)), and grant the [permissions](#permissions). The current relay keeps running on SQL Service Broker.
2. Pause writes to the table.
3. Let the relay drain the table's queue, until `SELECT COUNT(*) FROM dbo.[Chatter_Queue_<SourceEventType>]` returns 0.
4. Seed the checkpoint at the end of the transaction log, so CDC relays only changes committed after the pause. Every change made before the pause has a lower LSN, whether or not the capture job has read it yet, so there's no need to wait for the job. Run it in the source database as a member of `db_owner`:

   ```sql
   INSERT INTO Chatter.Chatter_RelayCheckpoints (SourceName, CaptureInstance, LastLsn, UpdatedAt)
   SELECT N'orders-cdc', N'dbo_Orders', CONVERT(binary(10), REPLACE(log_end_lsn, ':', ''), 2), SYSUTCDATETIME()
   FROM sys.dm_db_log_stats(DB_ID());
   ```

   Don't seed it with `sys.fn_cdc_get_max_lsn()`: that's the last change the capture job has read, so if the job is behind, CDC also relays changes that SQL Service Broker already relayed.

5. Drop SqlChangeFeed's trigger, then its services and queues:

   ```sql
   DROP TRIGGER dbo.[Chatter_ChangeFeedTrigger_<SourceEventType>];
   DROP SERVICE [Chatter_Service_<SourceEventType>];
   DROP SERVICE [Chatter_DeadLetterService_<SourceEventType>];
   DROP QUEUE dbo.[Chatter_Queue_<SourceEventType>];
   DROP QUEUE dbo.[Chatter_DeadLetterQueue_<SourceEventType>];
   ```

6. Deploy the relay with the CDC source configured ([Configure the relay](#configure-the-relay)) and the table's mappings switched to it with `Source`. Mappers don't change.
7. Resume writes.

To switch back, reverse the steps: install the trigger again (`UseChangeFeedSqlMigrationsAsync()` or SqlChangeFeed's install script), remove `Source` from the mappings, and disable CDC on the table (`sys.sp_cdc_disable_table`).

## Upgrading to 0.4.0

0.4.0 separates capturing changes from mapping and publishing them, so that other change sources (such as SQL Server CDC) can be added alongside SQL Service Broker. Existing configuration binds unchanged; code changes are needed:

- SQL Service Broker capture moved to the new `Chatter.IntegrationEventRelay.ServiceBroker` package. Add it and chain `.UseServiceBrokerChangeSource()` after `.AddIntegrationEventRelay()`. The `Action<SqlChangeFeedOptionsBuilder>` that `AddIntegrationEventRelay` accepted is now a parameter of `UseServiceBrokerChangeSource`.
- `UseChangeFeedSqlMigrationsAsync()` moved to the `Chatter.IntegrationEventRelay.ServiceBroker.Extensions` namespace.
- Mappers receive a `RelayContext` (source, operation, table and, where the source has them, position and commit time) instead of `IMessageBrokerContext`: `MapAsync(MappingData<TSource> data, RelayContext context, EventMappingConfigurationItem? mappingConfig)`. The synchronous default `Map` method was removed.
- `ISourceEvent` extends Chatter's `IMessage` instead of `IEvent`. Source types only need to be classes; the SQL Service Broker source still needs `IMessage`, which `ISourceEvent` provides.
- `EventMappingConfigurationItem.SourceChangeType` is a `RowOperation` (`Insert`, `Update`, `Delete`), so configuration values are unchanged.
- Type names in configuration must be the full name, the assembly-qualified name, or a simple name that matches exactly one type. A partial namespace (for example `Event1.SourceEvents.Event1ChangedEvent`) no longer matches.
- The relay checks its configuration when the host starts and stops with a list of problems if a mapping's source isn't configured or registered, or a mapping doesn't have exactly one mapper.
- With SQL Service Broker, a source event type can be watched in only one table (SqlChangeFeed creates one queue per source event type). This is now checked at startup.
- Removed, with their replacements: `IRelayIntegrationEvent`/`IntegrationEventRelayService` (`IIntegrationEventPublisher`), `IEventMappingConfigItemProvider`, `IRowChangeHandlerExecutor`/`RowChangeExecutor` (`IRowChangeProcessor<TSource>`), `Row*EventHandler<TSource, TIntegrationEvent>` (`Row*EventHandler<TSource>` in the ServiceBroker package), `AddSqlChangeFeedPerUniqueDataSource`, `WireRowChangeEventHandlerFromConfig` and `WireHandlerExecutor`.
- Optional new configuration: named `Sources` (each with a `Type` and `ConnectionString`), a `Source` on each mapping, and `RequiredCapabilities` (for example `"Ordered"`) that the mapping's source must provide. Mappings without a `Source` keep using SQL Service Broker with the relay's `ConnectionString`.
- 0.4.0 also adds the CDC change source, `Chatter.IntegrationEventRelay.Cdc` (see [Using the CDC change source](#using-the-cdc-change-source)).

### Migrating mappers

Before (0.3.x):

```csharp
using Chatter.MessageBrokers.Context;

public class OrderShippedMapper : IMapSourceUpdateToIntegrationEvent<OrderChangedEvent, OrderShippedEvent>
{
    public Task<OrderShippedEvent?> MapAsync(MappingData<OrderChangedEvent> data, IMessageBrokerContext context, EventMappingConfigurationItem? mappingConfig)
    {
        ...
    }
}
```

After (0.4.0):

```csharp
using Chatter.IntegrationEventRelay.Core.Abstractions;

public class OrderShippedMapper : IMapSourceUpdateToIntegrationEvent<OrderChangedEvent, OrderShippedEvent>
{
    public Task<OrderShippedEvent?> MapAsync(MappingData<OrderChangedEvent> data, RelayContext context, EventMappingConfigurationItem? mappingConfig)
    {
        // context.SourceName, context.Operation, context.Table; context.Position and context.CommittedAt are null with SQL Service Broker
        ...
    }
}
```

Mappers that implemented only the synchronous `Map` must implement `MapAsync` instead (return `Task.FromResult(...)`). Anything read from the `IMessageBrokerContext` (for example the inbound message's headers) has no equivalent, since a change no longer arrives as a brokered message with every source.

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

### Change sources in the demo

The demo uses both change sources (see the Worker's [`appsettings.json`](Chatter.IntegrationEventRelay.Worker/src/appsettings.json) and [`Program.cs`](Chatter.IntegrationEventRelay.Worker/src/Program.cs)):

- `Event1` is captured with CDC (source `fakedb-cdc`). Its mappers turn soft deletes, restores and hard deletes into `Event1DeletedEvent` and `Event1RestoredEvent`, which needs old values and commit order. The SQL container runs with SQL Server Agent (`MSSQL_AGENT_ENABLED=true`), and [`.scripts/01-Enable-Cdc-Event1.sql`](.scripts/01-Enable-Cdc-Event1.sql) waits for the Agent, then enables CDC on `Event1` and creates the relay's tables with the scripts in `.scripts/cdc`. Both Workers run; one polls `Event1` and the other stands by.
- `Event2` is captured with SQL Service Broker (no `Source`, so the default). [`.scripts/02-Chatter-Install-Event2.sql`](.scripts/02-Chatter-Install-Event2.sql) installs its trigger and queues, and both Workers receive from the queue.

The 100 operations produce 50 `Event1DeletedEvent` (25 soft deletes and 25 hard deletes), 25 `Event1RestoredEvent` and 25 `Event2CreatedEvent`; `.devops/e2e.sh` checks those exact counts.

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
# Leave out the emulator (see docker-compose.azure.yml)
COMPOSE_FILE=docker-compose.yml:docker-compose.azure.yml
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

#### Add a change source

Changes are captured from the database by a change source (see [Choosing a change source](#choosing-a-change-source)). This walkthrough uses the SQL Service Broker change source:
`dotnet add package Chatter.IntegrationEventRelay.ServiceBroker`

To use CDC instead, add `Chatter.IntegrationEventRelay.Cdc` and follow [Using the CDC change source](#using-the-cdc-change-source).

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
using Chatter.IntegrationEventRelay.ServiceBroker.Extensions;

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
        .AddIntegrationEventRelay()
            //Capture changes to the configured tables with triggers and SQL Service Broker
            .UseServiceBrokerChangeSource();
    }).Build();

var env = host.Services.GetRequiredService<IHostEnvironment>();
if (env.IsDevelopment())
    //Creates all database dependencies, including Install/Uninstall stored procs, triggers and SQL Service Broker queues. Should only be used in a development environment. Created DB objects should be deployed by other means (i.e., dacpac).
    await host.Services.UseChangeFeedSqlMigrationsAsync();

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
    public class OrderChangedEvent : ISourceEvent //SQL Service Broker requires source events to implement ISourceEvent
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

Mappings that don't name a `Source` are captured by SQL Service Broker from the database in `ConnectionString`.

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
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;

namespace MyNewIntegrationEventRelay
{
    public class OrderShippedMapper : IMapSourceUpdateToIntegrationEvent<OrderChangedEvent, OrderShippedEvent>
    {
        public Task<OrderShippedEvent> MapAsync(MappingData<OrderChangedEvent> mappingData, RelayContext context, EventMappingConfigurationItem? mappingConfig)
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
