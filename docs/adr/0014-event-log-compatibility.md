# 0014. Event log compatibility

- Status: Accepted
- Date: 2026-11-22

## Context
The event log (`src/Twin.Sim/Events/EventLog.cs`) is JSON Lines with a schema version `v` on every line. It is
read back by the replay, the Godot viewer and the tests, and logs are kept on disk. Until now nothing said which
changes keep old logs readable, and two accidents could break them silently:
1. The `type` field was the C# class name, so renaming a record changed the format.
2. No test pinned the format: a writer change that also changed the reader still passed every round trip.

The format grew inside v1 without a rule: new optional fields (`mode`/`rounds`, `weaponClass`, `formation`,
`heavy`) and new event types (backlog 0004 S5a1, S5a2).

## Decision
1. **Every line carries `v`.** The reader reads every version it knows, through one branch per version in
   `EventLog.Read`. Old logs stay readable until an ADR retires their version. Forward compatibility (an old
   reader reading a newer log) is not promised.
2. **No version bump** for:
   - a new field whose absence reads as a default giving the old behaviour, as `mode`/`rounds`, `weaponClass`,
     `formation` and `heavy` already do;
   - a new event type. **DECISION NEEDED:** this default matches history (S5a1 and S5a2 added types inside v1).
     Overruling it means bumping `v` for each new type; only this line changes, not code.
3. **Bump `v` and add a reader branch** for anything else: renaming or removing a field or a type tag; changing
   a field's type, unit, frame or meaning; renaming an enum member (enums are written by name); changing how
   numbers are formatted.
4. **Type tags are fixed strings**, kept in `EventLog.Tags` apart from the C# names. A renamed record keeps its
   old tag.
5. **Each version has a committed golden log**, `tests/Twin.Sim.Tests/Core/Data/event-log-v<N>.jsonl`, pinned by
   `EventLogContractTests` (byte-identical round trip, a hand-checked snapshot at t = 100, every event type
   present). The golden log is **append-only**:
   - existing lines are never changed or removed; a change that needs that is a format change (rule 3);
   - an additive change appends lines with a time after the snapshot time (100 s);
   - a new optional field also gets a hand-written line without it in
     `Older_v1_lines_without_optional_fields_read_as_defaults`.
6. **Non-finite numbers are not part of the format.** Writing NaN or an infinity fails with an
   `ArgumentException` that names the event and the field.

## Alternatives considered
- **The C# class name as the tag** (as before): rejected; a rename silently changes the format.
- **JSON Schema files:** a second description of the format to keep in step with the code, and they would not
  catch number formatting or replay changes. The golden log tests the real writer and reader.
- **No version field:** rejected; a reader could not tell an old log from a broken one.

## Consequences
- Format changes become visible in review: either a new golden line (additive) or a new version with its own
  golden file and reader branch.
- The type-coverage test fails as soon as a new event type is added without a golden line, which is intended.
- The reader grows one branch per version; retiring a version takes an ADR.

## Future work
ADR 0015 proposes a three-stream model: an input log, a derived state stream and fact events. This ADR governs
the v1 fact-event log only; the streams that ADR adds will need their own compatibility rules.
