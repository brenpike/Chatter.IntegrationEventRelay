# Plan: CDC change source alongside SQL Service Broker

Status: decisions resolved; ready to start Phase 0.

## Goal
Keep the existing trigger + SQL Service Broker (SSB) capture and add SQL Server Change Data Capture (CDC)
as a second, independent capture mechanism. Each mapping chooses its source in configuration. Mappers,
mapping configuration and publishing are shared by both sources.

## Non-goals
- Change Tracking as a source (it records neither old values nor intermediate changes, so the existing
  transition-based mappers cannot work with it).
- Automatic switching to a new capture instance after a schema change (v1 warns; config change required).
- Publishing through anything other than Chatter.

## Target architecture

```
Path A (SSB):  Table -> Trigger -> SSB queue -> ServiceBrokerChangeSource --+
Path B (CDC):  Table -> Txn log -> CDC change table -> CdcChangeSource -----+
                                                                            v
                                      RowChange<T> -> IRowChangeProcessor<T> -> mapper -> IIntegrationEventPublisher
```

Rules:
- Each table/mapping uses exactly one source.
- The adapter owns acknowledgement (SSB: RECEIVE transaction commit; CDC: checkpoint save). The processor
  returns on success and throws on failure.
- Adapters declare capabilities; startup validation enforces mapping requirements.

## Branches / PRs

| # | Branch | Scope | Behaviour change |
|---|---|---|---|
| 0 | `feat/net10-ci` | .NET 10, CI repaired and run on PRs | None |
| 1 | `feat/change-source-abstraction` | Core abstractions; SSB moved behind `IChangeSource` | None (config backward compatible) |
| 2 | `feat/cdc-change-source` | CDC adapter package | Opt-in |
| 3 | `feat/cdc-sample-docs` | Demo mixes both sources; README | Demo/docs only |

Each branch is cut from `main` after the previous PR merges.

---

## Phase 0 - `feat/net10-ci`

Changes:
- All projects `net6.0` -> `net10.0` (.NET 10 LTS, supported to Nov 2028; .NET 8 support ends Nov 2026); bump `Microsoft.Extensions.*` and test packages accordingly.
- Core: `System.Data.SqlClient` -> `Microsoft.Data.SqlClient` (only `SqlConnectionStringBuilder` is used).
- `.github/workflows/cicd.yml`:
  - `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`.
  - Build + test on `pull_request` (`opened`, `synchronize`, `reopened`) to `main` and on push to `feat/**`.
  - Pack/deploy job only when a PR into `main` is merged.
  - Drop the path filter limiting CI to `Core/src/**` (tests and samples should also trigger it).

Acceptance: CI green on the PR itself; package contents unchanged apart from target framework.

---

## Phase 1 - `feat/change-source-abstraction`

### 1a. Spike (first commit, before design is locked)
Confirm `Chatter.MessageBrokers` 0.8.x `IBrokeredMessagePublisher` can publish an event with a destination
name, `PublishOptions` (content type, infrastructure type) and a caller-supplied message id, without an
inbound `IMessageBrokerContext`/transaction. Fallback: Chatter `IMessageDispatcher`/`IExternalDispatcher`.
Outcome recorded in the PR description.

### 1b. New core types (`Chatter.IntegrationEventRelay.Core/src/Abstractions/`)

```csharp
public enum RowOperation { Insert, Update, Delete }

public sealed record ChangePosition(string Value, IComparable? Order = null)
{
    public string ToMessageId() => Value;
}

public sealed record RowChange<TSource>(
    RowOperation Operation,
    TSource? OldValue,              // updates only
    TSource NewValue,               // deleted row for deletes
    SourceTable Table,
    ChangePosition? Position,       // null when the source has no positions (SSB)
    DateTimeOffset? CommittedAt)    // null when unknown (SSB)
    where TSource : class;

public sealed record RelayContext(
    string SourceName, RowOperation Operation, SourceTable Table,
    ChangePosition? Position, DateTimeOffset? CommittedAt, CancellationToken CancellationToken);

[Flags] public enum ChangeSourceCapabilities { None = 0, Ordered = 1, Resumable = 2, CommitTime = 4 }

public interface IChangeSource
{
    string Name { get; }
    ChangeSourceCapabilities Capabilities { get; }
}

public interface IRowChangeProcessor<TSource> where TSource : class
{
    Task ProcessAsync(RowChange<TSource> change, CancellationToken ct);
    Task ProcessBatchAsync(IReadOnlyList<RowChange<TSource>> changes, CancellationToken ct); // default: sequential loop
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, EventMappingConfigurationItem mapping, string? messageId, CancellationToken ct)
        where TEvent : class, IEvent;
}
```

`RowChangeProcessor<TSource>` replaces `RowChangeExecutor`: for a change it finds every mapping matching
(source, table, operation, source type), resolves the mapper for each integration event type, maps, and
publishes non-null results with `messageId = $"{position}:{integrationEventType}"` when a position exists.

### 1c. Mapper API (breaking -> 0.3.0)
```csharp
Task<TIntegrationEvent?> MapAsync(MappingData<TSource> data, RelayContext context, EventMappingConfigurationItem? mappingConfig);
```
- Keep `IMapSourceInsert/Update/DeleteToIntegrationEvent<,>`; remove the sync-over-async default `Map`.
- `ISourceEvent` no longer extends `IEvent`; source DTOs are plain classes. Keep `ISourceEvent` as an
  optional marker (or drop it; see decisions).

### 1d. Configuration (backward compatible)
```json
"IntegrationEventRelay": {
  "ConnectionString": "...",                       // legacy default, still honoured
  "Sources": {
    "fakedb-ssb": { "Type": "ServiceBroker", "ConnectionString": "..." },
    "fakedb-cdc": { "Type": "Cdc", "ConnectionString": "...", "PollInterval": "00:00:01" }
  },
  "Mappings": [
    { "Source": "fakedb-cdc", "TableName": "Event1", "SourceChangeType": "Update",
      "SourceEventTypeName": "...", "IntegrationEventTypeName": "...", "CaptureInstance": "dbo_Event1" }
  ]
}
```
- Mapping without `Source` -> implicit SSB source built from legacy `ConnectionString`/`DatabaseName`,
  so existing configs keep working unchanged.
- `SourceChangeType` binds to `RowOperation` (same strings: Insert/Update/Delete).
- Adapter-specific keys (`CaptureInstance`, `PollInterval`, ...) are read by the adapter, not by core.

### 1e. New project `Chatter.IntegrationEventRelay.ServiceBroker`
Moves from core:
- `Handlers/RowInserted|Updated|DeletedEventHandler` -> thin adapters: build `RowChange<T>`, call processor.
- `AddSqlChangeFeedPerUniqueDataSource`, `UseChangeFeedSqlMigrations`, `WireRowChangeEventHandlerFromConfig`.
- Dependency on `Chatter.SqlChangeFeed` and `Chatter.MessageBrokers.SqlServiceBroker` moves here.
- Capabilities: `None`. Acknowledgement unchanged (Chatter's receive transaction and recovery options).

### 1f. Builder API
```csharp
services.AddChatterCqrs(...).AddMessageBrokers(...).AddAzureServiceBus(...)
    .AddIntegrationEventRelay()               // returns IIntegrationEventRelayBuilder
        .UseServiceBrokerChangeSource();      // registers "ServiceBroker" source type
```

### 1g. Startup validation (fail fast, `IHostedService` that runs first)
- Every mapping's `Source` resolves to a configured source whose `Type` is registered.
- Source and integration event type names resolve to exactly one type (exact `FullName` or
  assembly-qualified match; replaces the `EndsWith` lookup).
- Exactly one mapper implementation per closed mapper interface (no silent last-wins).
- Mapping `RequiredCapabilities` (optional, e.g. `["Ordered"]`) satisfied by its source.

### 1h. Files affected
Core src: `ChatterBuilderExtensions.cs`, `EventMappingConfiguration*.cs`, `EventMappingConfigItemProvider.cs`,
`IEventMappingConfigItemProvider.cs`, `IntegrationEventRelayService.cs` (becomes the Chatter publisher),
`IRelayIntegrationEvent.cs`, `Handlers/*` (move or delete), `Mapping/*`, `ISourceEvent.cs`,
`Extensions/StringExtensions.cs`. Worker sample mappers and source DTOs (signature update only).
Tests: the 23 test/test-context files referencing these types are moved or updated.

### 1i. Tests
- Existing tests ported (processor replaces executor tests; handler tests move to the SSB test project).
- New: processor (multiple mappings per change, null result skipped, message id, mapper exception propagates),
  config binding incl. legacy config, startup validation cases.
- `ChangeSourceContractTests<TAdapter>` abstract xUnit base: delivers insert/update/delete as `RowChange<T>`
  with correct old/new values; failure in processor is not acknowledged; declared capabilities hold.
  SSB adapter runs it with a fake feed.

Acceptance: demo behaves identically (100 events, 50 per consumer); existing configs bind unchanged.

---

## Phase 2 - `feat/cdc-change-source`

### 2a. New project `Chatter.IntegrationEventRelay.Cdc`

| Component | Responsibility |
|---|---|
| `CdcSourceOptions` | ConnectionString, PollInterval (1s), MaxTransactionsPerBatch (500), StartPosition (`Earliest`/`Latest`), LeaseTimeout, ErrorPolicy (`Halt`/`ParkRow`), MaxAttemptsBeforePark. Relay tables live in the source database, in the relay-wide `Schema` setting (see 2h) |
| `CdcLsn` | `binary(10)` value type: compare, increment-aware, hex formatting |
| `SqlCdcQueries` | `sys.fn_cdc_get_max_lsn()`, `sys.fn_cdc_get_min_lsn(@ci)`, `sys.fn_cdc_increment_lsn(@lsn)`, batch upper bound from `cdc.lsn_time_mapping`, `cdc.fn_cdc_get_all_changes_<ci>(@from, @to, N'all update old')`, captured columns |
| `CdcRowReader<T>` | Maps captured columns to DTO properties by name (case-insensitive); pairs op 3/4 rows sharing `__$start_lsn` + `__$seqval` into one Update; ops 1/2 -> Delete/Insert |
| `SqlCheckpointStore` | `<Schema>.Chatter_RelayCheckpoints(SourceName, CaptureInstance, LastLsn binary(10), UpdatedAt datetime2)`; upsert after batch publish |
| `SqlAppLockLease` | `sp_getapplock` (session owner) on a dedicated connection; one active poller per capture instance, others standby |
| `CdcPollerService` | `BackgroundService`, one loop per capture instance (see 2b) |
| `CdcChangeSource` | `IChangeSource`, Capabilities = `Ordered \| Resumable \| CommitTime` |

Builder: `.UseCdcChangeSource()`; dev-only `EnsureCdcEnabled()` helper mirrors `UseChangeFeedSqlMigrations`.

### 2b. Poll loop
```
acquire lease (else wait LeaseTimeout, retry)
loop:
  last  = checkpoint ?? (StartPosition == Latest ? max_lsn at first start : null)
  from  = last is null ? min_lsn(ci) : increment_lsn(last)
  if last is not null and from < min_lsn(ci): raise CdcGapException (halt loop, critical log, unhealthy)
  to    = upper LSN of next MaxTransactionsPerBatch transactions > last (cdc.lsn_time_mapping), capped at max_lsn
  if to is null: delay PollInterval; continue
  rows  = all_changes(ci, from, to, 'all update old') ORDER BY __$start_lsn, __$seqval, __$operation
  changes = pair/convert rows -> RowChange<T> (CommittedAt from lsn_time_mapping.tran_end_time)
  processor.ProcessBatchAsync(changes)          // sequential, commit order
  checkpoint.Save(to)                           // only after full batch success
```
- At-least-once: crash before checkpoint -> batch replayed; message ids (`ci:lsn:seqval:op[:eventType]`)
  let Azure Service Bus duplicate detection drop replays (topic must have duplicate detection enabled).
- Graceful shutdown: finish in-flight publish, do not save a partial checkpoint.

### 2c. Error policy for a change that keeps failing
Transient failures (broker unavailable, SQL timeouts) are always retried with exponential backoff and never
parked; parking them would park everything during an outage. Policies below apply only to changes that keep
failing (mapper exceptions, serialization errors, rejected messages) after `MaxAttemptsBeforePark` attempts.

- `Halt` (default): stop the poll loop at the failing change, keep retrying with backoff, log critical,
  report unhealthy. Nothing after it is published, so ordering is never violated.
- `ParkRow` (opt-in): per-row parking.
  - The failing change is written to `<Schema>.Chatter_RelayParkedChanges(SourceName, CaptureInstance,
    RowKey, Lsn, SeqVal, Operation, Payload, Error, Attempts, ParkedAt)`.
  - Later changes for the same row key are also parked behind it (in order) instead of published.
  - All other rows continue normally and the checkpoint advances.
  - Replay (`relay` admin API / SQL procedure) republishes a row's parked changes in LSN order once the cause
    is fixed; on success they are removed and the row is unblocked.
  - Row key = the table's primary key columns, read from the capture instance's index
    (`sys.sp_cdc_help_change_data_capture` `index_name`); tables without a unique index cannot use `ParkRow`
    (startup validation error).
  - Metric + health check: count and age of parked rows.

### 2d. Edge cases to cover (verify in integration tests)
- `tran_end_time` is server-local `datetime`: convert with the server's offset (`SYSDATETIMEOFFSET()`) once per batch.
- Nullable SQL columns mapped to non-nullable DTO properties (e.g. `BoolData bit NULL` -> `bool`): use default, log once.
- DTO properties not in the capture instance's column list: warn at startup.
- LOB columns in update before-images and primary-key updates: confirm how CDC represents them; document.
- CDC enabled but no changes yet (`min_lsn` all zeros); capture instance dropped while running (error + backoff).

### 2h. Where relay tables live
- In the source database (backed up and restored with the data they describe).
- In the same schema as the rest of the relay's database assets, set once relay-wide:
  `IntegrationEventRelay:Schema`, default `Chatter`. Table names keep Chatter's `Chatter_` prefix.
- The same setting is passed to the Service Broker adapter's SqlChangeFeed options so both adapters put
  their objects in one schema. Note: the current demo creates Chatter's SSB objects in `dbo`, and
  SqlChangeFeed's `WithSchema` option may refer to the watched table's schema rather than the asset schema;
  Phase 1 confirms which, and if SqlChangeFeed cannot place its assets in a separate schema, the default
  becomes whatever schema SqlChangeFeed uses so everything still lands together.

### 2e. Observability
- `System.Diagnostics.Metrics`: changes processed, events published, batch duration, lag
  (now - CommittedAt of last checkpointed change).
- Optional `IHealthCheck`: unhealthy on gap, halted error policy, or lag above threshold.

### 2f. SQL scripts
- `.scripts/cdc/enable-cdc.sql` (`sys.sp_cdc_enable_db`, `sys.sp_cdc_enable_table` per table).
- `.scripts/cdc/create-relay-tables.sql` (checkpoints, parked changes) parameterised by schema.

### 2g. Tests
- Unit (fakes): LSN compare/increment, 3/4 pairing, batch transaction boundaries, checkpoint only after
  publish, no checkpoint on failure, gap detection, StartPosition, lease standby, Halt, ParkRow (row blocked behind parked change, other rows continue, replay order), transient errors never parked.
- Integration (Testcontainers, SQL Server 2022, `MSSQL_AGENT_ENABLED=true`): contract suite; restart
  mid-batch resumes with no loss and identical message ids for replays; two pollers -> one active;
  soft-delete then restore yields both events in order.
- CI: integration tests in a separate job.

---

## Phase 3 - `feat/cdc-sample-docs`
- Worker sample: `Event1` on CDC (its soft-delete/restore mappers need old values and ordering), `Event2` on SSB.
- `docker-compose.yml`: `MSSQL_AGENT_ENABLED=true` for `db`; seed scripts enable CDC on `Event1`; demo still
  expects 100 events / 50 per consumer.
- README: choosing a source, per-source delivery semantics, SSB -> CDC cutover (enable CDC, seed checkpoint at
  current max LSN, switch mapping `Source`, drop trigger), 0.2 -> 0.3 mapper migration.

---

## Risks
- `IBrokeredMessagePublisher` may require an inbound transaction context -> settled by spike 1a.
- Breaking mapper signature -> acceptable at 0.x; migration notes in README and release notes.
- Integration tests lengthen CI -> separate job.
- This cloud environment cannot build or test (.NET SDK host blocked, no Docker daemon); PRs are validated by CI
  unless network access is changed.

## Decisions (resolved)
1. Phase 0 first, targeting .NET 10 (not 8).
2. Breaking mapper API at 0.3.0, no `[Obsolete]` shims.
3. Four stacked branches.
4. Checkpoints (and parked changes) in the source database, in the relay-wide schema (default `Chatter`, overridable), shared with the other relay database assets.
5. Error policy: `Halt` default; per-row parking (`ParkRow`) opt-in. Transient failures always retried, never parked.
6. Keep `ISourceEvent` as an optional marker.
