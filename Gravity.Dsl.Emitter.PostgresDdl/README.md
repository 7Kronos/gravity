# Gravity.Dsl.Emitter.PostgresDdl

PostgreSQL DDL reference emitter for the Gravity DSL.

It produces two coordinated artifact sets per `.gravity` source:

- **`schema/`** — idempotent baseline DDL: one file per entity (carrying its
  `CREATE TABLE IF NOT EXISTS`, lifecycle-state enum, foreign-key constraints,
  and btree / GIN indexes), one file per namespace-scope value type (composite
  `CREATE TYPE`), one file per namespace-scope enum (`CREATE TYPE … AS ENUM`).
  Every block is wrapped in the appropriate idempotency envelope so re-applying
  the script is a no-op.
- **`migrations/`** — a per-entity-version migration ledger: `V1__<Entity>.sql`
  is the baseline `CREATE TABLE`; `V2__<Entity>.sql`, `V3__<Entity>.sql`, … are
  forward-only `ALTER TABLE ADD COLUMN IF NOT EXISTS` / `ALTER TYPE ADD VALUE
  IF NOT EXISTS` diffs derived from the additive-only evolution the DSL grammar
  guarantees.

Relations emit foreign-key columns: cardinality-one → `<relation>_id UUID
REFERENCES …` plus a btree index; cardinality-many → `<relation>_ids UUID[]`
plus a GIN index, matching the dominant query plans each cardinality
participates in.

The target PostgreSQL schema is configurable (`schema: public` by default).

Consumed alongside `Gravity.Dsl.MsBuild` via the same two-package
`<PackageReference>` layout the JSON Schema emitter documents:

```xml
<PackageReference Include="Gravity.Dsl.MsBuild"            Version="..." />
<PackageReference Include="Gravity.Dsl.Emitter.PostgresDdl" Version="..." />
```

PostgreSQL 14+ is the dialect floor. See
`specs/006-phase-5-postgres-ddl-emitter/spec.md` for the locked contract.
