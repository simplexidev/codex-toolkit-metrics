# Schema lifecycle

Schema artifacts use a stable family name plus a major version in the filename, and a
semantic version in each document's `schemaVersion` field. For example,
`evaluation-record-v1.schema.json` accepts `schemaVersion` `1.0.0`.
Runner inputs use the separately versioned `evaluation-plan-v1.schema.json` family and
currently accept plan `schemaVersion` `1.0`.

- Patch versions clarify validation without changing the accepted data shape.
- Minor versions add backward-compatible optional fields or enum values.
- Major versions may remove, rename, reinterpret, or make fields required and therefore
  use a new schema filename and C# migration.

Readers must reject unsupported major versions rather than guessing. Writers emit only
the current version. Published records are immutable: migration creates a new record,
preserves the source record, and records the source schema version in migration tooling
or release provenance. Migrations must be deterministic, idempotent, covered by fixtures,
and must never invent a measured value. A value that cannot be migrated faithfully is
represented with `kind: "unavailable"`, `value: null`, and a method explaining why.

The v1 dashboard aggregate schema is independent from the evaluation-record family. It
remains supported until a separately versioned public aggregation phase replaces it.
