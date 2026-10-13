# AGENTS.md

Guidance for coding agents and automated reviewers working in this repository.

## Review scope

### The docker-compose demo is a local development sample

`docker-compose.yml`, `docker-compose.azure.yml`, `.scripts/` (except `.scripts/cdc/`) and the
`Chatter.IntegrationEventRelay.Worker` and `Chatter.IntegrationEventRelay.Consumer` samples exist so that
`docker compose up` runs the relay end to end on a developer's machine, and so that CI's e2e job can assert on it.
They are not a deployment template.

- The demo connects as `sa` with a fixed password, publishes SQL Server's port, and runs SQL Agent. This is
  intentional: the Service Bus emulator needs `sa`, and the demo must work with no setup. Don't report these as
  security findings, and don't change them unless the task asks for it.
- Do report demo changes that break `docker compose up`, the e2e job's expected event counts, or what the README
  says the demo does.

Library code (`Core`, `ServiceBroker`, `Cdc`), the reusable scripts in `.scripts/cdc/`, and the README's guidance
for production use are in scope for security review.

### The CDC relay login's grants

The least-privilege grant list for the CDC change source appears in four places. Keep them in sync when one
changes:

- `.scripts/cdc/enable-cdc.sql` (comment block)
- `README.md`, "Using the CDC change source" > "Permissions"
- the permissions remark on `CdcChangeSource` (`Chatter.IntegrationEventRelay.Cdc/src/CdcChangeSource.cs`)
- `MustFailWhenTheLoginMayNotReadTheCapturedTable` in
  `Chatter.IntegrationEventRelay.Cdc/tests/Integration/WhenCapturingChangesFromSqlServer.cs`, which runs the relay
  under exactly those grants

The documented list grants what a source needs under any configuration (for example, the parked-change table is
needed only with `ErrorPolicy: ParkRow`, and the docs say so). A finding that one configuration needs less is a
documentation note, not a defect, unless the list grants something no configuration needs.
