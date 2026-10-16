# Integration Event Relay

Relays changes to a legacy SQL Server database's tables to message broker infrastructure as integration events, so systems that can't publish events themselves can take part in an event-driven architecture.

## Language

### Events

**Source event**:
A table row's values as captured by a change source, typed by the mapping's `SourceEventTypeName`.
_Avoid_: Domain event (when meaning the captured row), row event

**Integration event**:
The message the relay publishes to the broker after a mapper accepts a source event.
_Avoid_: Message, broker event

**Row change**:
One insert, update or delete of one row, with its old and new values and its position.
_Avoid_: Change event, CDC row

**Mapping**:
A configured pairing of a source table and change type with a source event type and an integration event type, read from `IntegrationEventRelay:Mappings`.
_Avoid_: Event mapping item, route

**Mapper**:
User code that decides whether a source event becomes an integration event and builds it.
_Avoid_: Converter, translator

### Change sources

**Change source**:
The component that captures a table's row changes and hands them to the relay, either SQL Service Broker or CDC.
_Avoid_: Feed, listener, adapter

**Source**:
A named, configured instance of a change source under `IntegrationEventRelay:Sources`, which mappings name in `Source`.
_Avoid_: Connection, channel

**Capabilities**:
The delivery guarantees a source provides: `Ordered`, `Resumable` and `CommitTime`.
_Avoid_: Features, guarantees

**Position**:
Where a row change sits in its source's stream; CDC derives message ids from it so a replay republishes the same ids.
_Avoid_: Offset, sequence number

### CDC

**Capture instance**:
CDC's record of one captured table and its captured columns, read by the relay through its change table.
_Avoid_: CDC table, capture

**Change table**:
The `cdc.<capture instance>_CT` table CDC's capture job fills from the transaction log.
_Avoid_: CT table, CDC log

**Checkpoint**:
The last LSN a source has fully published for a capture instance, stored in `<Schema>.Chatter_RelayCheckpoints`.
_Avoid_: Offset, bookmark, watermark

**Error policy**:
What a CDC source does with a change that keeps failing: `Halt` (stop and retry) or `ParkRow` (set the row's changes aside).
_Avoid_: Failure mode, retry policy

**Parked change**:
A change set aside under `ParkRow`, with later changes to the same row parked behind it until a replay is requested.
_Avoid_: Dead letter, poison message

**Lease**:
The SQL Server application lock that lets one relay instance poll a source's capture instance while the others stand by.
_Avoid_: Leader lock, mutex

**Gating role**:
The database role (`@role_name`) a login must belong to before CDC returns a capture instance's changes.
_Avoid_: CDC role, reader role

**Relay schema**:
The relay-wide `Schema` setting (default `Chatter`) holding the relay's checkpoint and parked-change tables in the source database.
_Avoid_: Chatter schema, CDC schema

## Relationships

- A **Mapping** names exactly one **Source** and one **Mapper**
- A **Source** is an instance of one **Change source** and provides a set of **Capabilities**
- A **Change source** produces **Row changes**; each carries one **Position**
- A **Mapper** turns a **Source event** into zero or one **Integration event**
- A CDC **Source** reads one **Capture instance** per mapped table, and keeps one **Checkpoint** and one **Lease** per **Capture instance**
- A **Capture instance** has exactly one **Change table** and at most one **Gating role**
- **Parked changes** and **Checkpoints** live in the **Relay schema**, shared by every **Source** that uses it
- `ParkRow` as **Error policy** drops `Ordered` from the **Source**'s **Capabilities**

## Example dialogue

> **Dev:** "If a **Mapper** throws for row 42 under `ParkRow`, does the **Checkpoint** stop advancing?"
> **Domain expert:** "No: the **Row change** becomes a **Parked change**, later changes to row 42 park behind it, and other rows keep publishing, so the **Checkpoint** advances. Under `Halt` the **Checkpoint** stays put and nothing after it is published."

## Flagged ambiguities

- "Domain event" appears in the README for both the captured row and the legacy system's business event. Resolved: the captured row is a **Source event**; the published message is an **Integration event**.
- "Source" means both the configured **Source** and the general idea of a **Change source**. Resolved: **Change source** is the kind (SQL Service Broker, CDC); **Source** is a named, configured instance of one.
