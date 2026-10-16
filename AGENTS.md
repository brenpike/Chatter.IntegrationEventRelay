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

The least-privilege grants for the CDC change source live in one script, `.scripts/cdc/grant-relay-permissions.sql`,
which derives them from CDC's metadata per capture instance and error policy. `MustRelayUnderExactlyTheGrantsOfTheGrantScript`
in `Chatter.IntegrationEventRelay.Cdc/tests/Integration/WhenCapturingChangesFromSqlServer.cs` runs the relay under exactly
the script's grants (`Halt` and `ParkRow`, each with and without a gated capture instance) and checks that it fails
with a permission error without any one of them. Change the script and the test together; a grant the relay doesn't
need fails the test.

`README.md` ("Using the CDC change source" > "Permissions") and the permissions remark on `CdcChangeSource`
(`Chatter.IntegrationEventRelay.Cdc/src/CdcChangeSource.cs`) describe the grants in prose. Keep them in step with the
script, but treat the script as the source of truth.

### The switch-over scripts

Switching a table between the SQL Service Broker and CDC change sources lives in two scripts,
`.scripts/cdc/switch-to-cdc.sql` and `.scripts/cdc/switch-to-service-broker.sql`. They are the source of truth for what
a switch waits for, refuses and changes. The integration tests in `Chatter.IntegrationEventRelay.Cdc/tests/Integration/`
run them against SQL Server: `WhenRelyingOnSqlServerToSwitchSources` checks the SQL Server behaviour they rely on and
defines the capture predicate both scripts use, and `WhenSwitchingToCdc`, `WhenSwitchingBackToServiceBroker` and
`WhenSwitchingThereAndBack` check that each change is relayed exactly once, by exactly one source. Change the scripts
and those tests together.

`README.md` ("Switching a table from SQL Service Broker to CDC") and each script's header comment describe the scripts in
prose. Keep them in step with the scripts, but treat the scripts as the source of truth. Like the rest of
`.scripts/cdc/`, the switch scripts are in scope for security review.
