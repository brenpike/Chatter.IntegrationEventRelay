# Handoff: CDC change source work

Last updated: 2026-10-12. Read this together with [`cdc-change-source.md`](cdc-change-source.md), which holds the full plan
and the resolved design decisions. Both files live on branch `claude/repo-overview-analysis-gekuwx`, not on `main`.

## Where things stand

| Item | State |
|---|---|
| Phase 0: PR #27, `feat/net10-ci` (.NET 10, latest Chatter packages, CI repaired) | **Merged** into `main` (squash), commit `e8150e1` |
| Phase 0.5: `feat/servicebus-emulator` (Service Bus emulator demo, e2e CI job) | Pushed, e2e green in CI, **no PR yet**, **not yet rebased onto `main`** |
| Phase 1: `feat/change-source-abstraction` | Not started |
| Phase 2: `feat/cdc-change-source` | Not started |
| Phase 3: `feat/cdc-sample-docs` | Not started |
| Open PRs | None |

## Next steps

1. **Rebase the emulator branch onto `main`.** #27 was squash-merged, so replay only the two emulator commits:
   ```bash
   git fetch origin main feat/servicebus-emulator
   git checkout -B feat/servicebus-emulator origin/feat/servicebus-emulator
   git rebase --onto origin/main 19c06ba   # replays 9986e95 and bf192b4
   ```
   Expected conflicts and how to resolve them:
   - `README.md`: keep `main`'s new "Upgrading to 0.3.0" section and its TOC entry. Take the emulator branch's
     rewritten "Running this repo locally" section, which removes the old Azure setup and `sql.env`/`worker.env`/`consumer.env` sections.
   - `docker-compose.yml`: take the emulator branch's version entirely. It already has `TrustServerCertificate=True`
     in the worker connection string and needs no env files.

   Validate with `dotnet build -c Release` (expect 0 warnings), `dotnet test` (expect 101 passing) and
   `docker compose config -q`. Then `git push --force-with-lease origin feat/servicebus-emulator`. This is our own
   branch and the user approved this flow.
2. **Open the PR** `feat/servicebus-emulator` → `main`. There is no PR template. The body should cover: the emulator
   (backed by the demo SQL Server, entities in `.devops/servicebus-emulator/Config.json`), conditional Entra ID auth,
   the SQL container fixes (`sqlcmd` path and a health check that waits for seeding), `.devops/e2e.sh` with the CI `e2e` job,
   and the README changes. Then subscribe to PR activity. Codex reviews PRs in this repo (the user comments `@codex review`).
3. **Phase 1** (`feat/change-source-abstraction`, cut from `main` after Phase 0.5 merges). Start with the spike in
   plan section 1a: confirm Chatter's `IBrokeredMessagePublisher` can publish without an inbound `IMessageBrokerContext`.
   **Version note:** the plan says the breaking mapper API ships as 0.3.0, but 0.3.0 was used by Phase 0, so Phase 1 is
   **0.4.0**. Update the plan document accordingly.

## Resolved decisions (details in the plan)

- Keep SQL Service Broker and add CDC as a second, independent capture adapter. Each mapping picks its source.
  **No Change Tracking:** it has no old values and no intermediate changes, and the mappers detect transitions.
- Target .NET 10. The breaking mapper API change has no `[Obsolete]` shims. Four stacked PRs on `feat/<slug>` branches.
- CDC checkpoints and parked changes live in the **source database**, in the same schema as the relay's other
  database objects (relay-wide `Schema` setting, default `Chatter`, overridable). Tables use the `Chatter_` prefix. Open item:
  confirm whether SqlChangeFeed's `WithSchema` controls where its objects go; the demo currently puts them in `dbo`.
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
- The demo's `.scripts/01-*`/`02-*` were generated by Chatter.SqlChangeFeed 0.8.x but work with 0.16 (proven by e2e).

## Working conventions with this user

- Feature branches: `feat/<slug>`. Commit with the session's attribution trailer. Don't open PRs unless asked (the user
  approved PRs for each phase). No model names in commits or PRs.
- Give a recommendation rather than a list of options. When a design question has two parts (for example "change X? keep both?"),
  answer both explicitly.
- The user is the Chatter author.
