# Handoff: CDC change source work

Last updated: 2026-10-12. Read this together with [`cdc-change-source.md`](cdc-change-source.md), which holds the full plan
and the resolved design decisions. Both files live on branch `claude/repo-overview-analysis-gekuwx`, not on `main`.

## Where things stand

| Item | State |
|---|---|
| Phase 0: PR #27, `feat/net10-ci` (.NET 10, latest Chatter packages, CI repaired) | **Merged** into `main` (squash), commit `e8150e1` |
| Phase 0.5: PR #28, `feat/servicebus-emulator` (Service Bus emulator demo, e2e CI job) | **Merged** into `main` (squash), commit `73b961f` |
| Phase 1: PR #29, `feat/change-source-abstraction` (abstractions, ServiceBroker package, 0.4.0) | **Merged** into `main` (squash), commit `9610e09` |
| Phase 2: `feat/cdc-change-source` | Not started (no branch yet) |
| Phase 3: `feat/cdc-sample-docs` | Not started |
| Open PRs | None |

## Next steps

1. **Phase 2** (`feat/cdc-change-source`), cut from `main` (#29 is merged). Start with `CdcLsn`, `SqlCdcQueries` and
   `CdcRowReader<T>` (plan 2a), then the poll loop (2b). Build on #29's notes in plan section 1a: the poller must create
   a DI scope per batch (the publisher is scoped), call `IRowChangeProcessor<T>.ProcessBatchAsync`, and pass
   `ChangePosition` so message ids are stable for replays. Write `ChangeSourceContractTests<TAdapter>` here.

## Resolved decisions (details in the plan)

- Keep SQL Service Broker and add CDC as a second, independent capture adapter. Each mapping picks its source.
  **No Change Tracking:** it has no old values and no intermediate changes, and the mappers detect transitions.
- Target .NET 10. The breaking mapper API change has no `[Obsolete]` shims. Four stacked PRs on `feat/<slug>` branches.
- CDC checkpoints and parked changes live in the **source database**, in the relay-wide `Schema` setting (default `Chatter`,
  overridable). Tables use the `Chatter_` prefix. SqlChangeFeed's `WithSchema` is both the watched table's schema and the
  schema of its trigger, queues and procedures, so SSB objects stay with each table (documented); the relay's own tables
  don't depend on that. Possible Chatter follow-up: an asset-schema option in SqlChangeFeed.
- Error policy: `Halt` by default, with **per-row parking** (`ParkRow`) as an opt-in. Transient failures are always retried and never parked.
- Keep `ISourceEvent` as an optional marker.

## Environment notes (cloud sessions)

- **.NET SDK:** `apt-get update && apt-get install -y dotnet-sdk-10.0`. The Microsoft install script host
  (`builds.dotnet.microsoft.com`) is blocked by the network policy. Suggest the user adds this command to the environment's setup script.
- **No Docker daemon.** The e2e and SQL/CDC integration tests run only in GitHub Actions (`e2e` job). The `docker compose`
  CLI is present, so `docker compose config -q` works for validation.
- **CI artifacts can't be downloaded here** (blob storage host blocked). `e2e.sh` prints each service's logs into the
  job log on failure, so read failures with the GitHub MCP `get_job_logs` tool.
- `learn.microsoft.com` is blocked. Docker Hub's API (`hub.docker.com/v2/repositories/...`) and `mcr.microsoft.com/v2/.../tags/list` work.
- **Inspecting Chatter packages:** they ship no XML docs. Download the `.nupkg` from `api.nuget.org/v3-flatcontainer`
  and decompile with `dotnet tool install ilspycmd --tool-path <scratchpad>/tools`.
- The Docker/emulator/SQL Server EULAs are accepted via `ACCEPT_EULA=Y` in compose. The README says so.

## Things learned the hard way

- **Scrutor 7** (pulled in by Chatter.CQRS 0.21) enforces `publicOnly: true` for `AddClasses`; Scrutor 3.3 ignored it.
  Mapper scanning passes `publicOnly: false` so `internal` mappers still register. Scrutor 7 also de-duplicates a type
  scanned from multiple assemblies.
- **Microsoft.Data.SqlClient** encrypts and validates certificates by default. The demo SQL Server needs `TrustServerCertificate=True`.
- `UseChangeFeedSqlMigrationsAsync` must use `ConfigureAwait(false)`, otherwise the obsolete sync wrapper deadlocks on
  single-threaded contexts (a Codex finding, fixed).
- Chatter's Azure Service Bus client: when the connection string has a shared access key, it calls `new ServiceBusClient(connectionString)`,
  so `UseDevelopmentEmulator=true` works. It never creates entities, so the emulator config must declare them.
- `e2e.sh` runs under `set -euo pipefail`; `grep` with no matches exits 1, so wrap it as `{ grep ... || true; }`.
- Newer SQL Server images ship `sqlcmd` at `/opt/mssql-tools18/bin/sqlcmd` (needs `-C`). The old health check passed
  before seeding finished. Both are fixed on the emulator branch.
- The e2e job (`.devops/e2e.sh`) now asserts exact per-type counts (50 `Event1DeletedEvent`, 25 `Event1RestoredEvent`,
  25 `Event2CreatedEvent`) after the count settles, so it catches mapping/routing regressions in Phase 1.
- The emulator's readiness probe is `http://servicebus:5300/health` (works on 2.0.1). BusyBox `nc -z` also works there,
  despite a Codex claim otherwise.
- **Phase 1 layout:** `Chatter.IntegrationEventRelay.Core` (package `Chatter.IntegrationEventRelay`; abstractions, processor,
  publisher, config, startup validation) and `Chatter.IntegrationEventRelay.ServiceBroker` (src + tests). ServiceBroker tests
  reference Core's test project for its `TestContext` mock creators. A CDC adapter should follow the same layout
  (`Chatter.IntegrationEventRelay.Cdc/src|tests`), be added to the `.sln` by hand (`dotnet sln add` rewrites platforms and
  adds a folder), to `Worker.Dockerfile` (it restores the whole solution) and to the CI publish/pack steps.
- **Scoped publisher:** `IIntegrationEventPublisher` depends on Chatter's scoped `IBrokeredMessageDispatcher`, so the
  processor and invokers are scoped. Anything that processes changes outside a Chatter receive (the CDC poller) must create
  a DI scope per batch.
- `Chatter.MessageBrokers.SqlServiceBroker` declares a type named `Microsoft.Extensions.DependencyInjection.Extensions`, which
  breaks `using Microsoft.Extensions.DependencyInjection.Extensions;` in projects referencing it (use `AddTransient` etc.).
- Positioned message ids are a 32-hex SHA-256 over length-prefixed source name, position, event type assembly name and
  full name (`MappingInvoker.GetMessageId`). Codex pushed on this three times; don't reintroduce concatenated ids.
- Codex reviews every push and often raises follow-ups on its own previous findings; verify each against a real path
  before fixing. It also wrongly claimed BusyBox `nc -z` doesn't work; CI disproved it.
- Force-pushing needs explicit approval from the user in the session (the auto-mode classifier blocks it otherwise).
- The demo's `.scripts/01-*`/`02-*` were generated by Chatter.SqlChangeFeed 0.8.x but work with 0.16 (proven by e2e).

## Working conventions with this user

- Feature branches: `feat/<slug>`. Commit with the session's attribution trailer. Don't open PRs unless asked (the user
  approved PRs for each phase). No model names in commits or PRs.
- Give a recommendation rather than a list of options. When a design question has two parts (for example "change X? keep both?"),
  answer both explicitly.
- The user is the Chatter author.
