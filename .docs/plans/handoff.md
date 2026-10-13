# Handoff: CDC change source work

Last updated: 2026-10-13. Read this together with [`cdc-change-source.md`](cdc-change-source.md), which holds the full plan
and the resolved design decisions. Both files live on branch `claude/repo-overview-analysis-gekuwx`, not on `main`.

## Where things stand

| Item | State |
|---|---|
| Phase 0: PR #27, `feat/net10-ci` (.NET 10, latest Chatter packages, CI repaired) | **Merged** into `main` (squash), commit `e8150e1` |
| Phase 0.5: PR #28, `feat/servicebus-emulator` (Service Bus emulator demo, e2e CI job) | **Merged** into `main` (squash), commit `73b961f` |
| Phase 1: PR #29, `feat/change-source-abstraction` (abstractions, ServiceBroker package, 0.4.0) | **Merged** into `main` (squash), commit `9610e09` |
| Phase 2: PR #30, `feat/cdc-change-source` (package `Chatter.IntegrationEventRelay.Cdc`) | **Merged** into `main` (squash), commit `7a4bd79` |
| Phase 3: PR #31, `feat/cdc-sample-docs` (demo: Event1 on CDC, Event2 on SSB; README; AGENTS.md) | **Merged** into `main` (squash), commit `db0f299` |
| Open PRs | none |
| Open follow-ups | #32 (generate the CDC grant list from one tested script per configuration), #33 (ship the SSB-to-CDC switch-over as scripts with integration tests in both directions) |

## Next steps

All planned phases are merged. Remaining work is the follow-up issues:

1. **#32:** the CDC relay login's grant list is written by hand in four places (see `AGENTS.md`). Replace it with one
   parameterized grant script and integration tests per configuration (`Halt` without the parked-change grant, `ParkRow` with it).
2. **#33:** the README's switch-over procedure (both directions) is prose that nothing tests. Ship it as scripts, or a
   relay-side handover, with integration tests covering an open writer at the pause, a non-empty dead-letter queue,
   parked rows, and idle and busy capture.

## Phase 3 notes

- Demo seed `.scripts/01-Enable-Cdc-Event1.sql` waits for `msdb.dbo.sp_is_sqlagent_starting` = 0 (wrapped in TRY/CATCH, since
  Agent XPs are off at first), then `:r`-includes `.scripts/cdc/*.sql`. `sys.dm_server_services` says `Running` too early.
- Local WSL session: Docker daemon already running, Docker Hub and NuGet in `docker build` work, so `./.devops/e2e.sh` runs locally.
- Process: run the local review loop (hivemind:local-reviewer) to a clean exit before every push, and after each push wait for
  both CI and the Codex GitHub review (chatgpt-codex-connector, a few minutes later) and handle its inline comments.
  Codex reads the repo's `AGENTS.md`, which marks the docker-compose demo as out of scope for security findings.
- Review rounds on hand-written grant lists and prose procedures kept finding one more edge case; that's why #32 and #33 exist.
- No git identity in the local shell; commit with `git -c user.name="Brennan Pike" -c user.email="5853674+brenpike@users.noreply.github.com"`.

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
- **Docker works if you start the daemon:** `(dockerd > /tmp/dockerd.log 2>&1 &)` (restart it the same way if it dies
  after heavy builds). Testcontainers integration tests then run locally:
  `dotnet test Chatter.IntegrationEventRelay.Cdc/tests --filter Category=Integration` (SQL Server 2022 + Agent).
  Limits: Docker Hub rate-limits anonymous pulls (busybox in the e2e compose fails), and `docker build` can't trust the
  proxy CA, so NuGet restore inside images fails; run the full e2e in CI. Unit tests: `--filter "Category!=Integration"`.
- Never run `pkill -f`/`pgrep -f` with a pattern that matches your own shell command line; it kills the shell.
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

## Phase 2 lessons (CDC, verified on SQL Server 2022)

- Read `cdc.<ci>_CT` directly, ordered by `__$start_lsn, __$command_id, __$seqval, __$operation`;
  `fn_cdc_get_all_changes` doesn't return `__$command_id`, and seqval alone doesn't order commands.
- A primary key update is a delete + insert with the same seqval but different command ids.
- `sys.fn_cdc_get_min_lsn` is NULL before the capture job's first scan, and 0x00... when the capture instance is
  missing **or** the login can't read it (no SELECT on the source table). Distinguish with `cdc.change_tables`.
- Cleanup commits the new min LSN before deleting rows, so re-reading the min LSN after a direct read detects
  cleanup during the read.
- `cdc.lsn_time_mapping` rows with `tran_id = 0x00` are capture-job heartbeats; move the checkpoint past them.
- A least-privilege login needs `GRANT SELECT ON SCHEMA::cdc` for metadata and change tables.
- T-SQL `EXEC (N'...' + QUOTENAME(...))` is invalid; build `@sql` and use `sp_executesql`.
- Codex reviewed ~13 rounds on #30; every finding was checked against a real SQL Server before fixing.
  The DST commit-time issue was documented, not fixed (user's call).

## Working conventions with this user

- Feature branches: `feat/<slug>` (CI triggers on `feat/**` pushes and PRs to `main`). Never add `claude/**` to CI
  triggers, and don't push work to `claude/*` branches; the plan docs on this branch are the exception. Commit with the session's attribution trailer. Don't open PRs unless asked (the user
  approved PRs for each phase). No model names in commits or PRs.
- Give a recommendation rather than a list of options. When a design question has two parts (for example "change X? keep both?"),
  answer both explicitly.
- The user is the Chatter author.
