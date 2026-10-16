# Integration Event Relay

Relays changes to a legacy SQL Server database's tables to message broker infrastructure as integration events, so systems that can't publish events themselves can take part in an event-driven architecture.

[![ci](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/cicd.yml/badge.svg)](https://github.com/brenpike/Chatter.IntegrationEventRelay/actions/workflows/cicd.yml)
[![Core](https://img.shields.io/nuget/v/Chatter.IntegrationEventRelay?label=Chatter.IntegrationEventRelay)](https://www.nuget.org/packages/Chatter.IntegrationEventRelay/)
[![ServiceBroker](https://img.shields.io/nuget/v/Chatter.IntegrationEventRelay.ServiceBroker?label=Chatter.IntegrationEventRelay.ServiceBroker)](https://www.nuget.org/packages/Chatter.IntegrationEventRelay.ServiceBroker/)
[![Cdc](https://img.shields.io/nuget/v/Chatter.IntegrationEventRelay.Cdc?label=Chatter.IntegrationEventRelay.Cdc)](https://www.nuget.org/packages/Chatter.IntegrationEventRelay.Cdc/)
[![License](https://img.shields.io/github/license/brenpike/Chatter.IntegrationEventRelay)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)

[Project site](https://brenpike.github.io/Chatter.IntegrationEventRelay/): the README told as a story in nine chapters.

[context-landscape-diagram]: https://github.com/brenpike/Chatter.IntegrationEventRelay/raw/main/.docs/assets/context-landscape-diagram.png "Context Landscape Diagram"

- [How it works](#how-it-works)
- [Features](#features)
- [Packages](#packages)
- [Requirements](#requirements)
- [Quick start](#quick-start)
  - [Create the Worker Service](#create-the-worker-service)
  - [Add the packages](#add-the-packages)
  - [Wire up the relay in `Program.cs`](#wire-up-the-relay-in-programcs)
  - [Create and seed the database](#create-and-seed-the-database)
  - [Create the source event](#create-the-source-event)
  - [Create the integration event](#create-the-integration-event)
  - [Add the mapping to `appsettings.json`](#add-the-mapping-to-appsettingsjson)
  - [Create the mapper](#create-the-mapper)
  - [Try it out](#try-it-out)
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
  - [The pause](#the-pause)
  - [Switching to CDC](#switching-to-cdc)
  - [Switching back to SQL Service Broker](#switching-back-to-sql-service-broker)
  - [Sealed checkpoints](#sealed-checkpoints)
  - [Dead letters](#dead-letters)
  - [Removing SqlChangeFeed's objects](#removing-sqlchangefeeds-objects)
  - [Switching limitations](#switching-limitations)
- [Running this repo locally](#running-this-repo-locally)
  - [Change sources in the demo](#change-sources-in-the-demo)
  - [Service Bus emulator](#service-bus-emulator)
  - [Using a real Azure Service Bus namespace](#using-a-real-azure-service-bus-namespace)
- [Building and testing](#building-and-testing)
  - [Contributing](#contributing)
- [Upgrading](#upgrading)
  - [Upgrading to 0.4.0](#upgrading-to-040)
  - [Upgrading to 0.3.0](#upgrading-to-030)
- [License](#license)

## How it works

The Integration Event Relay lets legacy systems take part in an event-driven architecture when their as-is architecture, technical debt, or inability to integrate with message broker infrastructure would otherwise keep them out. The legacy system must use SQL Server as its backend.

The relay watches database tables for row changes (an insert, update or delete of one row) with a *change source*. Each row change becomes a *source event*: the row's values, typed by the mapping's source event type. A *mapping* in configuration pairs a table and change type with a source event type and an integration event type, and each mapping has one *mapper*: your code that decides whether the source event becomes an *integration event* and builds it. The relay runs in a [Worker service](Chatter.IntegrationEventRelay.Worker) and publishes the integration events to message broker infrastructure.

Two change sources are available. Each mapping picks a *source*, a named, configured instance of one of them (see [Choosing a change source](#choosing-a-change-source)):

- **SQL Service Broker**, via [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed): a trigger on each watched table adds the modified rows to a SQL Service Broker queue, which the Worker consumes. The SQL Server must support SQL Service Broker (most PaaS offerings don't).
- **SQL Server Change Data Capture (CDC)**: the Worker polls the change tables CDC fills from the transaction log, in commit order, and resumes from a checkpoint. No trigger is installed on the table.

![Context Landscape Diagram][context-landscape-diagram]

## Features

- Two change sources, SQL Service Broker and CDC. One relay can use both, but each table uses only one.
- Mappings in configuration, each with exactly one mapper. The relay checks its configuration when the host starts and stops with a list of problems.
- Publishes through any Chatter message broker infrastructure implementation, such as [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus).
- With CDC: each table's changes in commit order, resuming from a checkpoint after downtime, the commit time of each change, and message ids derived from the change's position, so a replay republishes the same ids.
- With CDC: a `Halt` or `ParkRow` error policy for changes that keep failing.
- With CDC: several instances of the relay can run with the same configuration; a lease (a SQL Server application lock) lets one of them poll each capture instance while the others stand by.
- With CDC: metrics and a health check.
- Tested SQL scripts to enable CDC, grant the relay's login least-privilege permissions, and switch a table between change sources so that every change is relayed by only one of them.

## Packages

| NuGet package | Project | Purpose |
|---|---|---|
| [Chatter.IntegrationEventRelay](https://www.nuget.org/packages/Chatter.IntegrationEventRelay/) | [Chatter.IntegrationEventRelay.Core](Chatter.IntegrationEventRelay.Core) | Mapping configuration, mapper interfaces, the processor that maps row changes to integration events and publishes them, and the extension methods to build a Worker Service. It doesn't capture changes itself: a change source package does. |
| [Chatter.IntegrationEventRelay.ServiceBroker](https://www.nuget.org/packages/Chatter.IntegrationEventRelay.ServiceBroker/) | [Chatter.IntegrationEventRelay.ServiceBroker](Chatter.IntegrationEventRelay.ServiceBroker) | The SQL Service Broker change source. It uses [Chatter.SqlChangeFeed](https://github.com/brenpike/Chatter/tree/master/src/Chatter.SqlChangeFeed), which installs a trigger on each watched table that sends changed rows to a SQL Service Broker queue. |
| [Chatter.IntegrationEventRelay.Cdc](https://www.nuget.org/packages/Chatter.IntegrationEventRelay.Cdc/) | [Chatter.IntegrationEventRelay.Cdc](Chatter.IntegrationEventRelay.Cdc) | The CDC change source. It polls each capture instance's change table, hands the changes to the processor in commit order, and saves a checkpoint in the source database once a batch is published. |

The repo also contains two samples, [Chatter.IntegrationEventRelay.Worker](Chatter.IntegrationEventRelay.Worker) and [Chatter.IntegrationEventRelay.Consumer](Chatter.IntegrationEventRelay.Consumer), which the demo runs end to end (see [Running this repo locally](#running-this-repo-locally)).

## Requirements

- .NET 10 (`net10.0`).
- SQL Server as the legacy system's database. The relay is tested against SQL Server 2019 and 2022.
- For the SQL Service Broker change source, a SQL Server that supports SQL Service Broker. For the CDC change source, CDC and SQL Server Agent (see [Choosing a change source](#choosing-a-change-source)).
- A Chatter message broker infrastructure implementation, such as [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus).

## Quick start

This walkthrough builds a relay that publishes an `OrderShippedEvent` to Azure Service Bus when an order's status changes to `Shipped`, using the SQL Service Broker change source.

### Create the Worker Service

```bash
dotnet new worker -n MyNewIntegrationEventRelay
cd MyNewIntegrationEventRelay
rm Worker.cs
```

The relay runs its own background services, so the template's `Worker.cs` isn't needed.

### Add the packages

```bash
dotnet add package Chatter.IntegrationEventRelay
dotnet add package Chatter.IntegrationEventRelay.ServiceBroker
dotnet add package Chatter.MessageBrokers.AzureServiceBus
dotnet add package Chatter.MessageBrokers.AzureServiceBus.Auth
```

`Chatter.IntegrationEventRelay.ServiceBroker` is the SQL Service Broker change source. `Chatter.MessageBrokers.AzureServiceBus.Auth` adds Entra ID (AAD) auth; leave it out, along with the `UseAadTokenProviderWithSecret` call below, if your connection string carries a shared access key.

### Wire up the relay in `Program.cs`

Replace `Program.cs` with:

```csharp
using Chatter.IntegrationEventRelay.Core.Extensions;
using Chatter.IntegrationEventRelay.ServiceBroker.Extensions;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddChatterCqrs(hostContext.Configuration)
        .AddMessageBrokers(b => b.AddRecoveryOptions(r => r.UseExponentialDelayRecovery(5)))
        .AddAzureServiceBus(b =>
        {
            // Use Entra ID (AAD) auth only when an app registration is configured. Otherwise the connection
            // string must carry a shared access key.
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
        // Wires up the mapper of each mapping in configuration
        .AddIntegrationEventRelay()
            // Captures changes to the mapped tables with triggers and SQL Service Broker
            .UseServiceBrokerChangeSource();
    }).Build();

var env = host.Services.GetRequiredService<IHostEnvironment>();
if (env.IsDevelopment())
{
    // Creates the triggers, SQL Service Broker queues and install/uninstall procedures. Development only: deploy them
    // by other means (for example a dacpac) everywhere else.
    await host.Services.UseChangeFeedSqlMigrationsAsync();
}

await host.RunAsync();
```

### Create and seed the database

Connect to `(localdb)\MSSQLLocalDB` and run:

```sql
CREATE DATABASE [OrdersDB]
GO

USE [OrdersDB]
GO

CREATE TABLE [dbo].[Orders](
	[Id] [uniqueidentifier] NOT NULL,
	[CustomerId] [uniqueidentifier] NOT NULL,
	[ProductId] [uniqueidentifier] NOT NULL,
	[Quantity] [smallint] NOT NULL,
	[OrderStatus] [varchar](25) NULL,
	[OrderPlacedOn] [smalldatetime] NOT NULL,
	CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id] ASC)
)
GO

INSERT INTO [dbo].[Orders] ([Id], [CustomerId], [ProductId], [Quantity], [OrderStatus], [OrderPlacedOn])
VALUES ('52CBE9FF-2496-4F46-9331-7F122AA3D973', '99FD8C91-F197-48BA-995C-17BD89D4394D', '170310D2-6EF8-47B0-9B4C-62BA088EA7BE', 10, 'Pending', GETDATE())
GO
```

### Create the source event

The source event holds a row of the `Orders` table. The SQL Service Broker change source needs source events to implement `ISourceEvent`.

```csharp
using Chatter.IntegrationEventRelay.Core;

namespace MyNewIntegrationEventRelay;

public class OrderChangedEvent : ISourceEvent
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public string? OrderStatus { get; set; }
    public DateTime OrderPlacedOn { get; set; }
}
```

### Create the integration event

The integration event is the message the relay publishes; it is the message body. Integration events implement Chatter's `IEvent`, and `[BrokeredMessage]` tells Chatter which topic to publish to.

Create the `order-shipped-event` topic, with a subscription to see the relayed messages, in your Azure Service Bus namespace (see [Create Service Bus topics and subscriptions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-quickstart-topics-subscriptions-portal)).

```csharp
using Chatter.CQRS.Events;
using Chatter.MessageBrokers;

namespace MyNewIntegrationEventRelay;

[BrokeredMessage("order-shipped-event")]
public class OrderShippedEvent : IEvent
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public DateTime OrderPlacedOn { get; set; }
    public DateTime OccurredAt { get; set; }
}
```

### Add the mapping to `appsettings.json`

Set the Azure Service Bus section to your namespace (see [Using a real Azure Service Bus namespace](#using-a-real-azure-service-bus-namespace) for the auth options), and keep secrets out of source control, for example with [user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

The lone mapping maps the `OrderChangedEvent` source event of an `Update` to the `Orders` table to the `OrderShippedEvent` integration event. It names no `Source`, so SQL Service Broker captures the table from the database in `ConnectionString`. At minimum, each [EventMappingConfigurationItem](Chatter.IntegrationEventRelay.Core/src/Configuration/EventMappingConfigurationItem.cs) needs `SourceEventTypeName`, `IntegrationEventTypeName`, `TableName` and `SourceChangeType` (see the class for every property).

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Chatter": {
    "Infrastructure": {
      "AzureServiceBus": {
        "ConnectionString": "Endpoint=sb://<your-namespace>.servicebus.windows.net/",
        "Auth": {
          "ClientId": "<app registration client id>",
          "ClientSecret": "<app registration secret>",
          "Authority": "https://login.microsoftonline.com/<tenant id>/"
        }
      }
    }
  },
  "IntegrationEventRelay": {
    "ConnectionString": "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=OrdersDB;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False;ApplicationIntent=ReadWrite;MultiSubnetFailover=False",
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

### Create the mapper

The mapper decides whether a source event becomes an integration event. `MapAsync` returns a new integration event when the source event meets your criteria, and `null` otherwise; nothing is published for `null`. Implement the interface that matches the mapping's `SourceChangeType`:

- `Insert`: `IMapSourceInsertToIntegrationEvent<TSourceEvent, TIntegrationEvent>`
- `Update`: `IMapSourceUpdateToIntegrationEvent<TSourceEvent, TIntegrationEvent>`
- `Delete`: `IMapSourceDeleteToIntegrationEvent<TSourceEvent, TIntegrationEvent>`

```csharp
using Chatter.IntegrationEventRelay.Core.Abstractions;
using Chatter.IntegrationEventRelay.Core.Configuration;
using Chatter.IntegrationEventRelay.Core.Mapping;

namespace MyNewIntegrationEventRelay;

public class OrderShippedMapper : IMapSourceUpdateToIntegrationEvent<OrderChangedEvent, OrderShippedEvent>
{
    public Task<OrderShippedEvent?> MapAsync(MappingData<OrderChangedEvent> mappingData, RelayContext context, EventMappingConfigurationItem? mappingConfig)
    {
        OrderShippedEvent? orderShippedEvent = null;

        // mappingData.OldValue only has a value for an update
        if (mappingData.OldValue?.OrderStatus != "Shipped" && mappingData.NewValue?.OrderStatus == "Shipped")
        {
            orderShippedEvent = new OrderShippedEvent
            {
                Id = mappingData.NewValue.Id,
                CustomerId = mappingData.NewValue.CustomerId,
                ProductId = mappingData.NewValue.ProductId,
                OrderPlacedOn = mappingData.NewValue.OrderPlacedOn,
                OccurredAt = DateTime.UtcNow
            };
        }

        return Task.FromResult(orderShippedEvent);
    }
}
```

### Try it out

Run the Worker Service with `dotnet run`, put a breakpoint on the mapper's `return`, then ship the seeded order:

```sql
USE [OrdersDB]
GO

UPDATE [dbo].[Orders]
   SET [OrderStatus] = 'Shipped'
 WHERE Id = '52CBE9FF-2496-4F46-9331-7F122AA3D973'
GO
```

The mapper returns an `OrderShippedEvent`, which the relay publishes to the `order-shipped-event` topic. Check your subscription for the message.

To capture the table with CDC instead, see [Using the CDC change source](#using-the-cdc-change-source).

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

Run [`.scripts/cdc/enable-cdc.sql`](.scripts/cdc/enable-cdc.sql) in the source database for each table (set `@tableSchema` and `@tableName` at the top, and `@captureInstance` to the mapping's `CaptureInstance` if it sets one), and [`.scripts/cdc/create-relay-tables.sql`](.scripts/cdc/create-relay-tables.sql) once to create the relay's checkpoint and parked-change tables (set `@schema` if the relay's `Schema` isn't `Chatter`). Both scripts are safe to run again. SQL Server Agent must be running; while it is still starting, enabling CDC on the first table fails with error 14258. Then grant the relay's login what it needs ([Permissions](#permissions)).

In development, `await host.Services.UseCdcSqlMigrationsAsync()` does the same for every mapped table.

The capture instance's retention (`sys.sp_cdc_change_job @job_type = 'cleanup', @retention = <minutes>`, default 3 days) is how long the relay can be down without losing changes.

### Permissions

Run [`.scripts/cdc/grant-relay-permissions.sql`](.scripts/cdc/grant-relay-permissions.sql) in the source database as a member of `db_owner`, once per capture instance the relay reads. Set `@user` to the relay login's user (a user; the script refuses a role or group, since grants to it would reach every member), `@captureInstance`, `@schema` (the relay's `Schema`) and `@errorPolicy` (the source's `ErrorPolicy`) at the top; `@whatIf = 1` lists the statements without running them. The script reads the captured table and its captured columns from CDC's metadata and grants:

- `SELECT` on each captured column of the captured table, and on its change table. SQL Server checks the captured columns before it returns changes; without them it reports no changes rather than an error, which the relay detects and stops on.
- `SELECT` on CDC's metadata tables `cdc.lsn_time_mapping`, `cdc.change_tables` and `cdc.captured_columns`, plus `cdc.index_columns` with `ParkRow`.
- On the relay's tables: `SELECT` and `INSERT`, and `UPDATE` of only the columns the relay changes, on the checkpoint table; with `ParkRow`, the same plus `DELETE` on the parked-change table.
- Membership in the capture instance's gating role (`@role_name`), if it has one. A gating role can be any database role, and the login gets everything it may do, so the script adds the login only when `@gatingRole` gives the role's exact name (case and trailing spaces included); otherwise it stops and names it. Check what the role grants first. A role that `sp_cdc_enable_table` created holds only `SELECT` on the capture instance's change functions.

The integration tests run the relay under exactly these grants for each error policy, with and without a gating role, and check that it fails without any one of them.

The script only grants. It doesn't revoke what an earlier run granted and a source no longer needs (after switching from `ParkRow` to `Halt`, moving the relay's tables to another `Schema`, or re-creating the capture instance with fewer columns or another gating role), since the same login may relay other capture instances that still need it; revoke those grants by hand.

The script grants columns rather than the whole table: a table-level `GRANT SELECT ON dbo.Orders` also works, but it exposes columns left out of capture with `@captured_column_list`. Likewise `GRANT SELECT ON SCHEMA::cdc` is simpler than the change-table and metadata grants, but it lets the login read the change data of every other captured table in the database. The metadata tables show the names of every capture instance and captured column in the database, though not their data.

The grants isolate captured data per capture instance, not relay state. Every source whose relay uses the same `Schema` shares the checkpoint and parked-change tables, and the grants on them are table-wide: a login can read the parked payloads (row values) of every source in that `Schema`, move their checkpoints, and replay or delete their parked changes. Give each relay login, or each group of sources that trust each other, its own `Schema`. Per-source isolation within one `Schema` is tracked in [#34](https://github.com/brenpike/Chatter.IntegrationEventRelay/issues/34).

These grants limit what the login can read, not what it can publish. The relay trusts its own tables, and the login can write them: whoever holds its credentials can move a checkpoint to skip or re-send changes and, with `ParkRow`, park a forged change and request its replay. Protect the relay's connection string as you would the broker credential.

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

Two scripts switch a table between the change sources: [`.scripts/cdc/switch-to-cdc.sql`](.scripts/cdc/switch-to-cdc.sql) from SQL Service Broker to CDC, and [`.scripts/cdc/switch-to-service-broker.sql`](.scripts/cdc/switch-to-service-broker.sql) back. Each picks a switch point so that every change is relayed by only one of the two sources: the source you switch away from relays every change committed before it, and the source you switch to every change committed after it. Each source still delivers at least once (see [Delivery semantics](#delivery-semantics)), but a change relayed by both would be published twice, with different message ids.

Set the parameters at the top of the script and run it in the source database, on its own (not inside a transaction), as a member of `db_owner`. It needs no server permission. SQL Server Agent and CDC's capture job must be running. The script commits the switch in one transaction, or rolls all of it back and changes nothing.

### The pause

Each script takes a shared lock on the table, so writes to the table wait until it ends, including writes that arrive while it waits for the lock. A transaction that wrote to the table and is still open when the script starts must commit or roll back first. Reads aren't blocked, except for a moment at the end: disabling or enabling SqlChangeFeed's trigger waits for queries running on the table and holds up new ones until the script commits.

The pause normally lasts a few seconds (the capture job scans the log every 5 seconds by default). The scripts' waits for the relay, the capture job and the CDC sources end within about `@timeoutSeconds` (default 300), and so do the lock waits of the statements they run holding the lock, except their reads of `cdc.lsn_time_mapping` while they wait for the capture job, `switch-to-cdc.sql`'s read of the oldest and latest LSN it picks the checkpoint from, and `switch-to-service-broker.sql`'s reads of the catalog. Switching back adds the time SqlChangeFeed's install procedures and the table's triggers take to run once each at the end, which `@timeoutSeconds` doesn't cut short. Past `@timeoutSeconds` the script rolls back, changing nothing, and the table stays on the source it was on; the error says what it was waiting for. Cancelling the script, or any error, rolls it back too.

Those reads, and the checks a script makes before it locks the table, aren't bounded by `@timeoutSeconds`: one blocked by another transaction's lock waits until that transaction ends. Before the lock that holds up no writes; a check waits, for example, while a relay transaction is still writing its checkpoint, or CDC's cleanup job is moving the capture instance's oldest LSN. Holding the lock, a transaction of CDC's capture or cleanup job writing CDC's metadata, or one changing the catalog, holds up the read, and writes to the table with it, until it ends. If the script doesn't finish, cancel it: the scripts set `XACT_ABORT ON`, so SQL Server rolls it back and nothing changes.

### Switching to CDC

Before running `switch-to-cdc.sql`:

1. Enable CDC on the table, create the relay's tables ([Enable CDC](#enable-cdc)), and grant the relay's login ([Permissions](#permissions)).
2. Keep the relay running with the table's mappings on SQL Service Broker: the script waits for it to drain the table's queues.
3. Don't relay the capture instance with CDC as `@sourceName` yet. With no checkpoint, the source starts from its `StartPosition`, and from `Earliest` it relays changes that SQL Service Broker relays too. Nor relay the table with any other CDC source: another `SourceName` of the capture instance, or a source of the table's other capture instance (for example the old one after a [schema change](#limitations)). The script seeds only `@sourceName`'s checkpoint, and refuses while another source of the table has a checkpoint in `@relaySchema` that isn't [sealed](#sealed-checkpoints): remove those sources from the table's mappings and delete their checkpoints.

Set `@tableSchema`, `@tableName`, `@captureInstance` (the mapping's `CaptureInstance`), `@sourceName` (the CDC source's name in `Sources`), `@relaySchema` (the relay's `Schema`), `@keepDeadLetters` and `@timeoutSeconds`. Holding the lock, the script:

1. Waits until the relay has drained every SqlChangeFeed queue of the table (`Chatter_Queue_<SourceEventType>`), and the capture job has read the log past the moment it took the lock. A message the relay has received but not committed counts until it commits, or goes back to the queue if the relay rolls back.
2. Refuses if a dead-letter queue of the table holds messages (see [Dead letters](#dead-letters)).
3. Saves `@sourceName`'s checkpoint of the capture instance at the latest change CDC has captured, replacing an earlier or [sealed](#sealed-checkpoints) one, so CDC relays only changes committed after the switch.
4. Disables SqlChangeFeed's triggers on the table (`Chatter_ChangeFeedTrigger_<SourceEventType>`, one per source event type), and commits.

Then deploy the relay with the CDC source configured ([Configure the relay](#configure-the-relay)) and the table's mappings switched to it with `Source`. Mappers don't change. Changes committed after the switch wait in the change table until the relay reads it. A CDC relay that is already running with a checkpoint sealed by an earlier switch back can keep running: it relays from the new checkpoint at its next poll.

SQL Service Broker relays nothing more of the table. The table's queues, services, dead-letter queues and procedures stay, with the trigger disabled, so the table can be switched back; see [Removing SqlChangeFeed's objects](#removing-sqlchangefeeds-objects) to remove them for good.

The script refuses, changing nothing, inside a transaction; when the capture instance doesn't exist or captures another table, or the relay's tables don't exist in `@relaySchema`; when the table has no SqlChangeFeed trigger, or one is disabled (the table is on CDC already, so running the script again after it succeeded is refused); when SQL Service Broker is disabled in the database, or a trigger's service or queue is missing or disabled; and when a CDC source of the table other than `@sourceName`'s of the capture instance has a checkpoint in `@relaySchema` that isn't sealed, since it would relay the table's changes alongside SQL Service Broker before the switch and `@sourceName` after it.

### Switching back to SQL Service Broker

`switch-to-service-broker.sql` switches back a table that `switch-to-cdc.sql` switched. Before running it:

1. Keep the relay running with the table's mappings on CDC: the script waits for every CDC source with a checkpoint for the capture instance in `@relaySchema` to catch up. A source stopped on a gap ([Gaps](#limitations)) can't catch up, and the script refuses it: CDC cleanup has removed changes it hadn't relayed, and sealing its checkpoint would hide them.
2. Make sure every CDC source of the capture instance has saved a checkpoint in `@relaySchema`, and start no other source of it until you've redeployed without the CDC source. The script sees a source only by its checkpoint: it waits for and seals the checkpoints that exist. A source with no checkpoint saves one at its first poll with `StartPosition` `Latest`, but with `Earliest` only once it has relayed its first batch. One that saves it while the script runs can be sealed before it has relayed every change committed before the switch, and one that saves it after the seal relays changes committed after the switch, which SQL Service Broker relays too. The same goes for every other CDC source of the table: if the table has another capture instance (for example the old one after a [schema change](#limitations)), seal or delete the checkpoints of its sources in `@relaySchema` and remove those sources from the table's mappings. The script seals only `@captureInstance`'s checkpoints, and refuses while a source of the other capture instance has a checkpoint that isn't sealed.
3. Replay or delete the capture instance's parked changes ([Error policy and parked changes](#error-policy-and-parked-changes)). Nothing would relay them after the switch, so the script refuses while any is parked, including one parked while it waits.
4. Don't deploy the relay with the table's mappings back on SQL Service Broker yet. Its SqlChangeFeed migrations re-create, enabled, a trigger whose table's columns changed while the table was on CDC; both sources would then relay the table's changes, and the script refuses a table with an enabled trigger.

Set `@tableSchema`, `@tableName`, `@captureInstance`, `@relaySchema` and `@timeoutSeconds`. Holding the lock, the script:

1. Waits until the capture job has read the log past the moment it took the lock, and then until every CDC source with a checkpoint for the capture instance in `@relaySchema` has relayed the capture instance's last change. Before it takes the lock, and on every pass of that wait, it refuses if CDC cleanup has passed a source's checkpoint (a gap).
2. Refuses if a change of the capture instance was parked while it waited.
3. [Seals](#sealed-checkpoints) those checkpoints.
4. Runs each trigger's install procedure (`Chatter_InstallChangeFeed_<SourceEventType>`), if it still exists, which re-creates a trigger whose table's columns have changed, and enables SqlChangeFeed's triggers on the table.
5. Fires the triggers with a statement that changes no row (or, to fire an `INSERT` trigger on a table whose columns are all generated, inserts a row of default values), and rolls that back, so a trigger that no longer compiles fails the switch rather than every write after it. The table's other `INSTEAD OF` triggers are disabled for that statement only, since they would replace it and the `AFTER` triggers would never fire; rolling back to the probe's savepoint enables them again, so they are never seen disabled outside the switch. Then it commits.

Then deploy the relay with `Source` removed from the table's mappings, and the CDC source removed if no other mapping uses it. Changes committed after the switch wait in the table's queue until the relay reads it.

The script refuses, changing nothing, inside a transaction; when the capture instance doesn't exist or captures another table, or the relay's tables don't exist in `@relaySchema`; when the table has no SqlChangeFeed trigger, or one is enabled (the table isn't on CDC); when SQL Service Broker is disabled in the database, or a trigger's service or queue is missing or disabled; when no CDC source has a checkpoint for the capture instance in `@relaySchema`; when a source of the table's other capture instance has a checkpoint in `@relaySchema` that isn't sealed, since it would keep relaying the table alongside SQL Service Broker; while a change of the capture instance is parked; and when CDC cleanup has passed a source's checkpoint of the capture instance (a [gap](#limitations): a non-zero checkpoint below the capture instance's oldest LSN), checked before the script takes the lock and again while it waits. A checkpoint of 0 starts from the oldest change and is never a gap. It rolls back when the capture job or a CDC source doesn't catch up in time, for example a source halted on a change it can't publish, or one that no longer runs: delete the checkpoints of sources that no longer run. If a trigger fails because a column it reads changed while the table was on CDC and its install procedure no longer exists, the script rolls back and says so: re-create the trigger for the table's current columns, disable it, and run the script again.

CDC keeps capturing the table. Disabling it is a separate, manual step: run `sys.sp_cdc_disable_table` once no relay reads the capture instance. To switch to CDC again after that, enable CDC on the table again first.

### Sealed checkpoints

`switch-to-service-broker.sql` sets `LastLsn` of each checkpoint of the capture instance in `@relaySchema` to `0xFFFFFFFFFFFFFFFFFFFE`, past any LSN SQL Server will write. A CDC relay still running with the table's mappings keeps polling the capture instance, stays healthy with a lag of 0, and relays none of its later changes. Remove the CDC source from the table's mappings when you redeploy. If you switch the table to CDC again, `switch-to-cdc.sql` replaces `@sourceName`'s seal with a new checkpoint.

### Dead letters

A change that ran out of receive attempts is in the table's dead-letter queue (`Chatter_DeadLetterQueue_<SourceEventType>`), and nothing relays it from there. `switch-to-cdc.sql` refuses while a dead-letter queue of the table holds messages, since nothing relays them after the switch either. Relay them yourself, or copy them out (`SELECT * INTO dbo.Orders_DeadLetters FROM dbo.[Chatter_DeadLetterQueue_<SourceEventType>]` keeps every message, `message_body` included), then run the script with `@keepDeadLetters = 1`: it switches the table and leaves them where they are.

### Removing SqlChangeFeed's objects

Neither script drops SqlChangeFeed's objects. Once the table is on CDC and won't be switched back, they can be removed for good:

1. Deal with the dead-letter queue first ([Dead letters](#dead-letters)): dropping it deletes its messages.
2. Make sure no relay receives from the table's queues (a relay deployed with the table's mappings on CDC doesn't): a receive waiting on a queue holds up `DROP QUEUE`.
3. For each source event type, run the uninstall procedure, if it exists. It drops the trigger, the services and queues, ending their conversations, and both procedures:

   ```sql
   EXEC dbo.[Chatter_UninstallChangeFeed_<SourceEventType>];
   ```

   Or drop them yourself:

   ```sql
   DROP TRIGGER dbo.[Chatter_ChangeFeedTrigger_<SourceEventType>];
   DROP SERVICE [Chatter_Service_<SourceEventType>];
   DROP SERVICE [Chatter_DeadLetterService_<SourceEventType>];
   DROP QUEUE dbo.[Chatter_Queue_<SourceEventType>];
   DROP QUEUE dbo.[Chatter_DeadLetterQueue_<SourceEventType>];
   DROP PROCEDURE dbo.[Chatter_InstallChangeFeed_<SourceEventType>];
   DROP PROCEDURE dbo.[Chatter_UninstallChangeFeed_<SourceEventType>];
   ```

Leave the message type `//Chatter/BrokeredMessage` and the contract `//Chatter`: every table on SQL Service Broker in the database uses them. Once the objects are gone, `switch-to-service-broker.sql` can't switch the table back.

### Switching limitations

- `switch-to-cdc.sql` seeds the checkpoint of one source, `@sourceName`, and refuses while another CDC source of the table has a checkpoint that isn't sealed. Another CDC source that should relay the same capture instance has no checkpoint (or a sealed one): before starting it, insert or update its checkpoint with the LSN the script printed as `LastLsn`. The script doesn't see checkpoints of relays that use another `Schema`, nor a source that hasn't saved its first checkpoint yet.
- `switch-to-service-broker.sql` waits for and seals every checkpoint of the capture instance in `@relaySchema`, including those of sources that no longer run (delete them first: the script times out waiting for them, or refuses one whose checkpoint CDC cleanup has passed). It doesn't see checkpoints of relays that use another `Schema` (nor, for the table's other capture instance, refuse them), nor a source that hasn't saved its first checkpoint yet (see [Switching back to SQL Service Broker](#switching-back-to-sql-service-broker)).
- To fire an `INSERT`-only SqlChangeFeed trigger on a table whose columns are all generated, `switch-to-service-broker.sql` inserts a real row of default values and rolls it back. If that row breaks a constraint, for example a `CHECK` or foreign key that the next identity value fails, the script refuses, changing nothing; its error reports the trigger as failing and quotes the constraint's error.
- A table switched to CDC by dropping SqlChangeFeed's objects, as an earlier version of this README described, is already on CDC: `switch-to-cdc.sql` refuses it, and `switch-to-service-broker.sql` can't switch it back.
- While the table is on CDC, anything that re-creates or re-enables SqlChangeFeed's trigger (a dacpac or migration that deploys it, or the relay's `UseChangeFeedSqlMigrationsAsync()` with the table's mappings on SQL Service Broker after the table's columns changed) makes both sources relay the table's changes. Leave the trigger out of such deployments, or remove SqlChangeFeed's objects for good.
- While either script runs, don't deploy the relay or SqlChangeFeed migrations for the table, and don't change its SqlChangeFeed services, queues, dead-letter queues or procedures. The scripts read the table's triggers before they lock the table, so a trigger created in between isn't switched: `switch-to-cdc.sql` leaves it enabled, so both sources relay its event type's changes, and `switch-to-service-broker.sql` leaves it disabled, so nobody does. The table lock doesn't hold off changes to SqlChangeFeed's services, queues, dead-letter queues or procedures, which aren't the table's.
- Disabling CDC on the table after switching back stays manual (`sys.sp_cdc_disable_table`).

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

The [Worker](Chatter.IntegrationEventRelay.Worker) sample shows how to build a relay with these packages. It publishes with [Chatter.MessageBrokers.AzureServiceBus](https://github.com/brenpike/Chatter/tree/master/src/Chatter.MessageBrokers.AzureServiceBus); another Chatter message broker infrastructure implementation can be used by changing its [`Program.cs`](Chatter.IntegrationEventRelay.Worker/src/Program.cs). The [Consumer](Chatter.IntegrationEventRelay.Consumer) sample handles the integration events with [IMessageHandler<>](https://github.com/brenpike/Chatter/blob/master/src/Chatter.CQRS/src/Chatter.CQRS/IMessageHandler.cs) implementations, logs each one, and displays the events it handled on shutdown. Both Consumers receive from each topic's `sub1` subscription, so they split the 100 integration events between them; `.devops/e2e.sh` counts the events handled across both.

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

## Building and testing

You need the .NET 10 SDK, and Docker for the integration tests and the end-to-end check. From the root of the repo:

```bash
dotnet build -c Release
dotnet test -c Release --filter "Category!=Integration"
```

The integration tests start SQL Server 2022 in Docker with Testcontainers:

```bash
dotnet test Chatter.IntegrationEventRelay.Cdc/tests/Chatter.IntegrationEventRelay.Cdc.Tests.csproj -c Release --filter "Category=Integration"
```

The end-to-end check runs the demo (see [Running this repo locally](#running-this-repo-locally)):

```bash
./.devops/e2e.sh
docker compose down -v
```

### Contributing

The scripts in `.scripts/cdc/` are the source of truth for the CDC grants and the switch-over; change them together with their integration tests (see [AGENTS.md](AGENTS.md)). Open pull requests against `main`. The project site, `web/index.html`, retells this README; keep it in step with the README, which stays the source of truth. A merge to `main` that changes `web/` deploys it.

## Upgrading

### Upgrading to 0.4.0

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

#### Migrating mappers

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

### Upgrading to 0.3.0

- Targets .NET 10 (`net10.0`); .NET 6 is no longer supported.
- Uses the latest Chatter packages, which connect to SQL Server with `Microsoft.Data.SqlClient` instead of `System.Data.SqlClient`. `Microsoft.Data.SqlClient` encrypts connections and validates the server certificate by default. If your SQL Server does not present a trusted certificate (for example a local or containerized SQL Server with a self-signed certificate), connections that worked before will now fail. Either provision a trusted certificate, or add `TrustServerCertificate=True` to the connection string (development only; it disables certificate validation).
- `UseChangeFeedSqlMigrations()` is obsolete; use `await UseChangeFeedSqlMigrationsAsync()`.

## License

MIT. See [LICENSE](LICENSE).
