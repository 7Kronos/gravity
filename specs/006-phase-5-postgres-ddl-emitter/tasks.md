# Gravity DSL — Task Plan (Phase 5: PostgreSQL DDL reference emitter)

**Status:** Locked for implementation
**Date:** 2026-05-20
**Driven by:** `specs/006-phase-5-postgres-ddl-emitter/plan.md`
**Predecessor:** `specs/004-phase-4-json-schema-emitter/tasks.md` (T300..T369). Phase 5 task ids begin at `T400` to keep the global task-id namespace grep-friendly; the T370..T399 band remains reserved for Phase 4 follow-ups.

Conventions:
- Tasks numbered `T4##` in execution order. Sub-phase boundaries (P5a → P5b → P5c) are hard gates; a sub-phase's tasks complete before the next begins.
- `[P]` marks tasks runnable in parallel with peers in the **same** sub-phase.
- Every task lists: **Acceptance** (verifiable; cites FR/AC from `spec.md`), **Files** (repo-relative paths touched), **Depends on** (prior T-numbers or `—`).
- Phase 0–3 / Phase 4 / Phase 8 / Phase 9 tasks remain locked. No Phase 5 task removes or weakens a predecessor acceptance condition. Phase 5 is strictly additive at the new-project layer.

---

## Sub-phase P5a — Project scaffold + IEmitter contract (T400–T412)

Goal: stand up `Gravity.Dsl.Emitter.PostgresDdl/` as a NuGet-packable sibling to `Gravity.Dsl.Emitter.JsonSchema/`, declare the `IEmitter` shell with the right target name / annotation namespace / config schema / supported AST range, and wire it into the solution + emitter-host discovery surface. No DDL generation happens here. Closes FR-400, FR-401, FR-402, FR-403, FR-404, FR-405, FR-460, FR-461, FR-462, FR-463, FR-464. AC-5.1, AC-5.2. LD-18, LD-19, LD-20.

### T400. Scaffold `Gravity.Dsl.Emitter.PostgresDdl/` csproj
- **Acceptance.** New project `Gravity.Dsl.Emitter.PostgresDdl/Gravity.Dsl.Emitter.PostgresDdl.csproj` targets `net9.0`. Property group sets `<IsPackable>true</IsPackable>`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`. ProjectReferences point to `Gravity.Dsl.Ast` and `Gravity.Dsl.Emitter` only — no reference to `Gravity.Dsl.Compiler`, `Gravity.Dsl.Emitter.CSharp`, `Gravity.Dsl.Emitter.JsonSchema`, or `Gravity.Dsl.Cli` (FR-400, FR-463). Inherits repo-wide `Directory.Build.props` (deterministic build, banned-APIs analyzer scope, treat-warnings-as-errors) without carve-out. Builds cleanly.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Gravity.Dsl.Emitter.PostgresDdl.csproj`.
- **Depends on.** —

### T401 [P]. Wire `Gravity.Dsl.Emitter.PostgresDdl` into the solution
- **Acceptance.** `Gravity.Dsl.sln` gains the new project entry via `dotnet sln add`. The solution still builds cleanly. The PostgreSQL emitter project is NOT referenced by `Gravity.Dsl.Tests` directly via `ProjectReference` at this stage; tests under P5c will add the reference.
- **Files.** `Gravity.Dsl.sln`.
- **Depends on.** T400.

### T402 [P]. NuGet packaging metadata + determinism flags
- **Acceptance.** csproj sets `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, `<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>`. Sets `<PackageId>Gravity.Dsl.Emitter.PostgresDdl</PackageId>`, `<Description>PostgreSQL DDL reference emitter for the Gravity DSL — emits idempotent table creation SQL plus per-version migration ledgers; target schema configurable.</Description>`, `<PackageTags>gravity dsl postgres postgresql ddl emitter migrations</PackageTags>`. Apache-2.0 license. Per FR-461 the resulting `.nupkg` must be byte-identical across back-to-back packs on a clean tree; asserted by T462.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Gravity.Dsl.Emitter.PostgresDdl.csproj` (extend).
- **Depends on.** T400.

### T403. `PostgresDdlEmitter.cs` — IEmitter skeleton
- **Acceptance.** `Gravity.Dsl.Emitter.PostgresDdl/PostgresDdlEmitter.cs` declares `public sealed class PostgresDdlEmitter : IEmitter` with public parameterless ctor (FR-405). Exposes `TargetName => "postgres-ddl"`, `AnnotationNamespace => "postgres"`, `SupportedAstVersions = SemanticVersionRange.Parse(">=1.0.0 <2.0.0")` (FR-401). Constants `ConfigKeyOutput`, `ConfigKeySchema`, `ConfigKeyMigrationPrefix`, `DefaultSchema = "public"`, `DefaultMigrationPrefix = "V"`, `TargetNameValue = "postgres-ddl"`. `Emit(model, config, sink)` body is a stub that returns `new EmitResult(ImmutableArray<Diagnostic>.Empty)` and does not yet walk declarations; P5b lands the walk. No `partial`, no `virtual`. Per plan §3.1.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/PostgresDdlEmitter.cs`.
- **Depends on.** T400.

### T404. `PostgresDdlEmitterConfig.cs` — config schema + typed projection
- **Acceptance.** `PostgresDdlEmitter.ConfigurationSchema` exposes exactly three `ConfigKey` entries per FR-402: `output` (String, required), `schema` (String, optional, default `"public"`), `migration_prefix` (String, optional, default `"V"`). `PostgresDdlEmitterConfig.cs` declares `internal sealed class PostgresDdlEmitterConfig` with `Output` / `Schema` / `MigrationPrefix` properties and `static From(EmitterConfig, ImmutableArray<Diagnostic>.Builder)` factory. Invalid `schema` value (not matching `[a-z_][a-z0-9_]*`, length > 63) appends `PG001` and returns `null` per FR-470. Diagnostic uses the synthetic-target-name span pattern from `HOST001` / `HOST002`. Per plan §3.2.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/PostgresDdlEmitter.cs` (extend `ConfigurationSchema`), `Gravity.Dsl.Emitter.PostgresDdl/PostgresDdlEmitterConfig.cs`.
- **Depends on.** T403.

### T405 [P]. `PgRuleIds.cs` — PG001..PG010 constants
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/PgRuleIds.cs` declares `internal static class PgRuleIds` with constants `Pg001`..`Pg010`. Each ruleId documented with FR mapping: `PG001` (invalid `schema` config; Error; FR-464), `PG002` (entity declares reserved `state` property; Error; FR-424 / FR-464), `PG003` (unknown / mismatched-type / reserved `@postgres` annotation key; Error; FR-441 / FR-464), `PG004` (invalid PG identifier in `@postgres(column: ...)` override; Error; FR-441 / FR-464), `PG005..PG010` reserved. Constants ship with the emitter assembly only; NOT added to `Gravity.Dsl.Compiler/RuleIds.cs`.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/PgRuleIds.cs`.
- **Depends on.** T400.

### T406 [P]. `Render/Identifier.cs` skeleton + IsValidPgIdentifier
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/Render/Identifier.cs` declares `internal static class Identifier`. Implements `static bool IsValidPgIdentifier(string s)` (regex `^[a-z_][a-z0-9_]*$`, length 1..63, culture-invariant). Implements `static string ToSnakeCase(string identifier)` per FR-451 acronym-aware algorithm (worked examples: `firstName → first_name`, `FirstName → first_name`, `HTTPSResponse → https_response`, `URL → url`, `first_name → first_name` idempotent, `Employee → employee`). No reserved-word check in v1 (the PG identifier rule alone is enough; reserved-word collisions are a deferred enhancement). Per plan §3.3, §3.4.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/Identifier.cs`.
- **Depends on.** T400.

### T407 [P]. `Render/SqlWriter.cs` skeleton
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/Render/SqlWriter.cs` declares `internal static class SqlWriter`. Constants: `Lf = "\n"`, `Indent = "    "` (4 spaces — FR-454). Functions: `string Compose(string header, params string[] blocks)` joining non-empty blocks with `Lf + Lf` and ensuring the result ends with exactly one trailing `Lf`. Helper `string IndentBlock(string body)` prepends `Indent` to each line. No timestamp, no machine-name, no clock — banned-APIs analyzer will catch any regression. Per plan §3.9.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/SqlWriter.cs`.
- **Depends on.** T400.

### T408 [P]. `Render/TypeMapper.cs` skeleton
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/Render/TypeMapper.cs` declares `internal static class TypeMapper`. Method signatures only (bodies in P5b):
  - `static string MapPrimitive(PrimitiveKind kind)` — returns PG column type per FR-430.
  - `static string MapType(TypeRef t, PostgresDdlEmitterConfig cfg, ResolvedModel model, IReadOnlySet<string> multiVersionFqns)` — full DSL type → PG column type string (no nullability suffix).
  - `static (string ColumnName, string ColumnTypeSnippet, bool IsArrayMany) MapRelation(RelationDecl r, PostgresDdlEmitterConfig cfg, ResolvedModel model, IReadOnlySet<string> multiVersionFqns)` — relation → column name + type + flag whether it's a many (drives GIN vs btree index choice).
  - Signatures compile against AST + emitter host. Bodies stubbed to `throw new NotImplementedException();` so P5b can land them.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/TypeMapper.cs`.
- **Depends on.** T400.

### T409 [P]. `Render/AnnotationFolder.cs` skeleton
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/Render/AnnotationFolder.cs` declares `internal static class AnnotationFolder`. Record `internal sealed record FoldedAttributes(string? OverrideColumnName, bool MarkUnique, bool MarkIndexed, string? DefaultExpression)`. Method signature: `static FoldedAttributes Fold(ImmutableArray<AnnotationDecl> annotations, string propertyName, string entityFqn, ImmutableArray<Diagnostic>.Builder diags)`. Stub returns `new FoldedAttributes(null, false, false, null)` so P5b can land the real folder. Per plan §3.8.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/AnnotationFolder.cs`.
- **Depends on.** T400.

### T410. Registration test `RegistrationTests.cs`
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/PostgresDdl/RegistrationTests.cs` instantiates `EmitterRegistry.FromInstances(new IEmitter[] { new PostgresDdlEmitter() })` and asserts FR-401 contract: `TargetName == "postgres-ddl"`, `AnnotationNamespace == "postgres"`, `SupportedAstVersions.Satisfies(new SemanticVersion(1, 0, 0)) && SupportedAstVersions.Satisfies(new SemanticVersion(1, 1, 0))`. Second test method registers `PostgresDdlEmitter` alongside a stub emitter that also claims `"postgres"` and asserts `HOST002` is surfaced. Third method registers `PostgresDdlEmitter` alongside `JsonSchemaEmitter` and `CSharpEmitter` and asserts zero ownership diagnostics (the three namespaces are disjoint). Adds `<ProjectReference>` from `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` to the new emitter project. Includes a unit-test block for `Identifier.IsValidPgIdentifier` (positive: `public`, `hr_prod`, `tenant_42`, `_v1`; negative: empty, `1bad`, `Public`, `bad-name`, 64-char string) and for `Identifier.ToSnakeCase` (per worked examples in T406).
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/RegistrationTests.cs`, `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj`.
- **Depends on.** T403, T406.

### T411 [P]. README.md in the emitter project
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/README.md` — one paragraph: "This package is the Gravity DSL PostgreSQL DDL reference emitter. It produces (a) idempotent baseline DDL files under `schema/` (one per entity table, one per value-type composite type, one per enum type) and (b) a per-entity-version migration ledger under `migrations/`. The target PG schema is configurable (`schema: public` by default). Relations emit foreign-key columns (cardinality-one → btree index) or array columns (cardinality-many → GIN index). Consumed alongside `Gravity.Dsl.MsBuild` via the same two-package `<PackageReference>` layout the JSON Schema emitter documents."
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/README.md`.
- **Depends on.** —

### T412 [P]. `buildTransitive/Gravity.Dsl.Emitter.PostgresDdl.props`
- **Acceptance.** New file `Gravity.Dsl.Emitter.PostgresDdl/buildTransitive/Gravity.Dsl.Emitter.PostgresDdl.props` mirrors the JSON Schema emitter's `.props` shape exactly — contributes `Gravity.Dsl.Emitter.PostgresDdl.dll` to the `<GravityDslEmitterAssembly>` MSBuild item (Phase 9 FR-224). csproj `<ItemGroup>` includes the file with `Pack="true" PackagePath="buildTransitive\Gravity.Dsl.Emitter.PostgresDdl.props"`. Per FR-460.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/buildTransitive/Gravity.Dsl.Emitter.PostgresDdl.props`, `Gravity.Dsl.Emitter.PostgresDdl/Gravity.Dsl.Emitter.PostgresDdl.csproj` (extend).
- **Depends on.** T402.

---

## Sub-phase P5b — DDL + migration generation (T420–T435)

Goal: implement every renderer behind the contract from P5a. By end of P5b the emitter produces a complete file tree against `samples/registry/` with zero diagnostics on the happy path; P5c then locks the bytes. Closes FR-420..FR-428, FR-430..FR-435, FR-440..FR-443, FR-450..FR-455, FR-470. AC-5.7 (configurable schema), AC-5.8 (migrations), AC-5.10 (relation indexes), AC-5.11 (annotation folding), AC-5.12 (annotation validation). LD-21, LD-22.

### T420. `TypeMapper.MapPrimitive` + `MapType` — closed-form mapping
- **Acceptance.** `TypeMapper.MapPrimitive(PrimitiveKind)` implements FR-430's closed table: `String → TEXT`, `Int → INTEGER`, `Long → BIGINT`, `Decimal → NUMERIC`, `Boolean → BOOLEAN`, `Date → DATE`, `DateTime → TIMESTAMPTZ`, `Uuid → UUID`. `MapType(TypeRef, …)` composes: `PrimitiveTypeRef` → `MapPrimitive(kind) + (IsArray ? "[]" : "")` (FR-432); `NamedTypeRef` → `cfg.Schema + "." + Identifier.ToSnakeCase(name)` for single-version, or `cfg.Schema + "." + Identifier.ToSnakeCase(name) + "_v" + version` when the referent is multi-version (FR-435). Optionality (`IsOptional`) does NOT change the type — it controls the `NOT NULL` clause at the column-render layer (FR-431). Closes FR-430, FR-431, FR-432, FR-435.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/TypeMapper.cs` (extend skeleton from T408).
- **Depends on.** T406, T408.

### T421. `TypeMapper.MapRelation` — `_id` / `_ids` suffix + index hint
- **Acceptance.** `MapRelation(RelationDecl)` returns `(ColumnName, ColumnTypeSnippet, IsArrayMany)`:
  - Cardinality `one` → `(Identifier.ToSnakeCase(r.Name) + "_id", "UUID", false)`.
  - Cardinality `many` → `(Identifier.ToSnakeCase(r.Name) + "_ids", "UUID[]", true)`.
  
  Target identity primitive is assumed `UUID` (the documented norm in `samples/registry/`); a future fixture exercising non-UUID identities will extend this mapper. Closes FR-433 / FR-434 (mapper half).
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/TypeMapper.cs` (extend).
- **Depends on.** T420.

### T422. `AnnotationFolder.Fold` — claimed keys + per-key value-type contracts
- **Acceptance.** `Fold(...)` implements FR-440 / FR-441:
  - **Claimed keys**: `column` (string → override column name; if not a valid PG identifier, emit `PG004`), `unique` (boolean), `index` (boolean), `default` (string, emitted verbatim).
  - **Reserved-not-consumed**: `precision`, `scale`, `max_length`, `storage`, `index_method`, `partial_where` → emit `PG003` ("reserved for future use; not consumed in v1").
  - **Unknown keys**: emit `PG003` ("unknown @postgres key '<key>'; the postgres namespace claims {column, index, unique, default}").
  - **Value-type mismatch** on a claimed key (e.g. `column: 42`, `unique: "yes"`): emit `PG003` naming offending key, value type observed, expected type.
  
  Returns a populated `FoldedAttributes` record with overrides applied; on diagnostic, the returned attributes still carry safe defaults so the caller can continue rendering other properties. Diagnostic span = the offending `AnnotationDecl.Span` (FR-470). Closes FR-440, FR-441, FR-443 (emitter-side enforcement).
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/AnnotationFolder.cs` (extend skeleton from T409).
- **Depends on.** T406, T409.

### T423. `Render/LifecycleStateRenderer.cs` — per-entity state enum
- **Acceptance.** New file declares `internal static class LifecycleStateRenderer`. `Render(EntityDecl entity, PostgresDdlEmitterConfig cfg, DeclKey key, bool multiVersion)` returns the idempotent `DO $$ BEGIN CREATE TYPE <schema>.<entity_name>[_v<N>]_state AS ENUM ('<variant>', ...); EXCEPTION WHEN duplicate_object THEN NULL; END $$;` block per FR-424. Variants in DSL declaration order (FR-422). Variant string literals use **single quotes** with no escaping (DSL identifier rule `[A-Za-z][A-Za-z0-9_]*` guarantees no internal quotes). Closes FR-424 (renderer half).
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/LifecycleStateRenderer.cs`.
- **Depends on.** T406, T407.

### T424. `Render/EnumRenderer.cs` — namespace-scope enum types
- **Acceptance.** New file declares `internal static class EnumRenderer`. `Render(EnumDecl en, DeclKey key, PostgresDdlEmitterConfig cfg, bool multiVersion)` returns the full baseline file per FR-422: header comment (three lines, no timestamp/machine name) + idempotent `DO $$ BEGIN CREATE TYPE <schema>.<enum_name>[_v<N>] AS ENUM (...); EXCEPTION WHEN duplicate_object THEN NULL; END $$;`. Variants in DSL declaration order; literals single-quoted (FR-422). Closes FR-422.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/EnumRenderer.cs`.
- **Depends on.** T407.

### T425. `Render/ValueTypeRenderer.cs` — namespace-scope composite types
- **Acceptance.** New file declares `internal static class ValueTypeRenderer`. `Render(ValueTypeDecl vt, ResolvedModel model, DeclKey key, PostgresDdlEmitterConfig cfg, bool multiVersion, IReadOnlyDictionary<string, SourceFile> declToFile, ImmutableArray<Diagnostic>.Builder diags)` returns the full baseline file per FR-421: header comment + idempotent `DO $$ BEGIN CREATE TYPE <schema>.<type_name>[_v<N>] AS (<field-list>); EXCEPTION WHEN duplicate_object THEN NULL; END $$;`. Field column names via `Identifier.ToSnakeCase` (FR-451). Field types via `TypeMapper.MapType` (FR-430..FR-432). Fields in DSL declaration order (FR-452). PG composite-type field declarations do NOT carry NOT NULL — composite fields are always nullable in PG (an enforced-NOT-NULL on a composite field is a deferred enhancement; documented in NG-8 deferrals). Closes FR-421.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/ValueTypeRenderer.cs`.
- **Depends on.** T406, T407, T420.

### T426. `Render/EntityTableRenderer.cs` — full table + state enum + indexes
- **Acceptance.** New file declares `internal static class EntityTableRenderer`. `Render(EntityDecl entity, ResolvedModel model, DeclKey key, PostgresDdlEmitterConfig cfg, bool multiVersion, IReadOnlyDictionary<string, SourceFile> declToFile, ImmutableArray<Diagnostic>.Builder diags)` composes the full entity file body per FR-420 / FR-423 / FR-424 / FR-433 / FR-434 / FR-453:
  1. Header comment (three fixed lines).
  2. Inline lifecycle state enum block (delegates to `LifecycleStateRenderer`).
  3. `CREATE TABLE IF NOT EXISTS <schema>.<table>[_v<N>] (...)` with columns in fixed order: identity → state → properties (DSL order) → relations (DSL order) → `PRIMARY KEY (<identity-col>)`.
     - Identity column: `<id_col> <pg-type> NOT NULL` (FR-423 step 1).
     - State column: `state <schema>.<table>[_v<N>]_state NOT NULL` (FR-423 step 2, FR-424).
     - Property column: `<col> <pg-type> [NOT NULL] [UNIQUE] [DEFAULT <expr>]` — nullability per FR-431, UNIQUE / DEFAULT folded from `@postgres` annotations via `AnnotationFolder.Fold` (FR-440..FR-441).
     - Relation cardinality-one column: `<rel>_id UUID [NOT NULL]` (FR-433). FK constraint emitted as a separate `ALTER TABLE` block (see step 4).
     - Relation cardinality-many column: `<rel>_ids UUID[] [NOT NULL DEFAULT '{}'::UUID[]]` (FR-434).
  4. Foreign-key constraint blocks (one `DO $$ BEGIN ALTER TABLE … ADD CONSTRAINT fk_<table>_<rel> FOREIGN KEY (<col>) REFERENCES <schema>.<target>(<target-id>); EXCEPTION WHEN duplicate_object THEN NULL; END $$;` per cardinality-one relation). Sorted ordinally by constraint name (FR-453).
  5. Index blocks (one `CREATE INDEX IF NOT EXISTS ix_<table>_<col>[_id|_ids] ON <schema>.<table>(<col>)` for btree, `... USING GIN (<col>)` for GIN; plus one per `@postgres(index: true)` property; de-duplicated by index name). Sorted ordinally by index name (FR-453).
  
  Pre-flight: if any `PropertyDecl.Name` (lowered to snake_case) equals `state`, emit `PG002` and skip the entity (FR-424). Closes FR-420, FR-423, FR-424, FR-433, FR-434, FR-453 (composition half).
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/EntityTableRenderer.cs`.
- **Depends on.** T406, T407, T420, T421, T422, T423.

### T427. `Render/MigrationRenderer.cs` — V1 baseline + V<N> diffs
- **Acceptance.** New file declares `internal static class MigrationRenderer`. `RenderAll(IReadOnlyList<(DeclKey, TopLevelDecl)> versions, ResolvedModel model, PostgresDdlEmitterConfig cfg, string? ns, IReadOnlyDictionary<string, SourceFile> declToFile, IEmitterOutput sink, ImmutableArray<Diagnostic>.Builder diags)` walks the versions in ascending order:
  - **For `EntityDecl` versions:**
    - V1 (lowest version): baseline migration file = full `EntityTableRenderer.Render` body. Written to `<output>/migrations/<ns>/<prefix>1__<EntityName>.sql`.
    - V<N> for N ≥ 2: diff migration. Compute structural diff between `vN-1` and `vN`: `newProps = vN.Properties.Where(p => vN-1.Properties.All(q => q.Name != p.Name))`, similarly for `newRelations` and `newStates`. Emit:
      - `ALTER TABLE <schema>.<table_vN> ADD COLUMN IF NOT EXISTS <col> <pg-type> [NOT NULL] [UNIQUE] [DEFAULT <expr>];` per `newProp`.
      - `ALTER TABLE <schema>.<table_vN> ADD COLUMN IF NOT EXISTS <rel>_id UUID [NOT NULL];` plus the matching FK constraint envelope plus btree index per cardinality-one `newRelation`. (Sorted ordinally by column name in the emitted file.)
      - `ALTER TABLE <schema>.<table_vN> ADD COLUMN IF NOT EXISTS <rel>_ids UUID[] [NOT NULL DEFAULT '{}'::UUID[]];` plus GIN index per cardinality-many `newRelation`.
      - `ALTER TYPE <schema>.<table_vN>_state ADD VALUE IF NOT EXISTS '<NewState>';` per new lifecycle state.
    - Migration file ends with `\n` exactly once (FR-454).
  - **For `ValueTypeDecl` versions** (only when `versions.Count > 1` — FR-427): V1 = full `ValueTypeRenderer.Render`; V<N> diffs = `ALTER TYPE … ADD ATTRIBUTE IF NOT EXISTS <field> <pg-type>;` per new field.
  - **For `EnumDecl` versions** (only when `versions.Count > 1`): V1 = full `EnumRenderer.Render`; V<N> diffs = `ALTER TYPE … ADD VALUE IF NOT EXISTS '<variant>';` per new variant.
  
  All migration filenames are `<prefix><N>__<DeclName>.sql` under `migrations/<ns>/`. Closes FR-426, FR-427.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/Render/MigrationRenderer.cs`.
- **Depends on.** T423, T424, T425, T426.

### T428. `PostgresDdlEmitter.Emit` — walks model, dispatches renderers
- **Acceptance.** Replaces the T403 stub body with the orchestration logic from plan §3.1:
  1. Project config via `PostgresDdlEmitterConfig.From`; on `null`, return early.
  2. Group `model.Declarations` by FQN (preserving `(FQN ordinal, Version asc)` order per FR-403).
  3. Build `declToFile` mapping FQN → `SourceFile`.
  4. For each group: emit `schema/` files (one per version: single-version → unqualified filename, multi-version → `.v<N>.sql`) and call `MigrationRenderer.RenderAll` for the migrations.
  5. Schema-file paths via `SchemaPath(cfg, ns, decl, key, multiVersion)`: `<cfg.Output>/schema/<ns-path>/<DeclName>[.v<N>].sql`.
  6. All path strings are `/`-normalised before sink writes.
  
  Closes FR-403, FR-425.
- **Files.** `Gravity.Dsl.Emitter.PostgresDdl/PostgresDdlEmitter.cs` (extend T403 stub).
- **Depends on.** T404, T426, T427.

### T429 [P]. Path-ordering smoke test
- **Acceptance.** A test in `Gravity.Dsl.Tests/Emitter/PostgresDdl/PathOrderingTests.cs` runs the emitter against a 3-entity fixture and asserts the `BufferedEmitterOutput.Snapshot()` keys are sorted under `StringComparer.Ordinal` and that `migrations/...` paths sort **before** `schema/...` paths in the same namespace (FR-455). This is a structural property the host enforces; the test confirms the emitter does not violate it (e.g. by writing files with leading `./` or `\\` segments).
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/PathOrderingTests.cs`.
- **Depends on.** T428.

### T430. Cross-emitter coexistence — ownership and ordering
- **Acceptance.** Existing `Gravity.Dsl.Tests/Emitter/HostIntegrationTests.cs` and `AnnotationNamespaceOwnershipTests.cs` are extended to include `PostgresDdlEmitter` alongside `CSharpEmitter`, `JsonSchemaEmitter`, and the outline sample. No `HOST001` / `HOST002` raised — `csharp`, `json_schema`, `postgres`, `outline` are disjoint annotation namespaces. The four-emitter run against `samples/registry/` produces output with no cross-emitter path collisions (`csharp/...` and `json-schema/...` and `postgres-ddl/...` and `outline/...` are distinct subtrees beneath the host output root). Closes AC-5.2 ownership third assertion.
- **Files.** `Gravity.Dsl.Tests/Emitter/HostIntegrationTests.cs` (extend), `Gravity.Dsl.Tests/Emitter/AnnotationNamespaceOwnershipTests.cs` (extend).
- **Depends on.** T428.

### T431 [P]. `TypeMappingTests.cs` — primitive + modifier matrix (in-code assertions)
- **Acceptance.** Parameterised test in `Gravity.Dsl.Tests/Emitter/PostgresDdl/TypeMappingTests.cs` covers every row of FR-430 × the six modifier combinations from FR-431 / FR-432 (`T`, `T?`, `T[]`, `T[]?`, `T?[]`, `T?[]?`). For each, the test constructs a one-property fixture entity and asserts the emitted column declaration matches expectations: e.g. `Decimal` → `NUMERIC NOT NULL`; `Decimal?` → `NUMERIC` (no NOT NULL); `String[]` → `TEXT[] NOT NULL DEFAULT '{}'::TEXT[]`; `String[]?` → `TEXT[]` (no NOT NULL, no default). The `DateTime` row asserts `TIMESTAMPTZ` (not `TIMESTAMP`). Closes AC-5.6 in-code half (the golden half is T461).
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/TypeMappingTests.cs`.
- **Depends on.** T428.

### T432 [P]. `AnnotationTests.cs` — positive + negative cases
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/AnnotationTests.cs` covers:
  - **Positive**: `@postgres(column: "primary_email", unique: true, index: true)` on `email: String` produces column `primary_email TEXT NOT NULL UNIQUE` plus `CREATE INDEX IF NOT EXISTS ix_<table>_primary_email ON ...(primary_email)`. `@postgres(default: "'EMP-' || nextval('seq')")` produces `... DEFAULT 'EMP-' || nextval('seq')`. Closes AC-5.11.
  - **Negative**: 
    - `@postgres(unknown_key: "x")` → exactly one `PG003`.
    - `@postgres(column: 42)` → exactly one `PG003` (wrong value type).
    - `@postgres(column: "1bad")` → exactly one `PG004` (invalid identifier).
    - `@postgres(precision: 10)` → exactly one `PG003` (reserved key).
    - `@postgres(unique: "yes")` → exactly one `PG003` (wrong value type for boolean key).
  
  Each diagnostic message names offending key, value, property, entity FQN per FR-470. Closes AC-5.12.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/AnnotationTests.cs`.
- **Depends on.** T428.

### T433 [P]. `MigrationTests.cs` — two-version fixture per AC-5.8
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/MigrationTests.cs` constructs a fixture with two `EntityDecl` versions of the same FQN (v1 + v2 where v2 adds: two new properties, one new cardinality-one relation, one new lifecycle state). Asserts the resulting file tree:
  - `schema/<ns>/Employee.v1.sql` and `schema/<ns>/Employee.v2.sql` both present (FR-425).
  - `migrations/<ns>/V1__Employee.sql` content equals the full body of `Employee.v1.sql` (FR-426 baseline).
  - `migrations/<ns>/V2__Employee.sql` content carries exactly: two `ALTER TABLE … ADD COLUMN IF NOT EXISTS …` (per new property), one `ALTER TABLE … ADD COLUMN IF NOT EXISTS …_id UUID NOT NULL` + matching FK envelope + btree index, one `ALTER TYPE …_v2_state ADD VALUE IF NOT EXISTS '<NewState>'`. No `DROP`, no `ALTER COLUMN TYPE` anywhere in either migration. Closes AC-5.8.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/MigrationTests.cs`.
- **Depends on.** T427, T428.

### T434 [P]. `ConfigurableSchemaTests.cs` — schema name propagation + PG001
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/ConfigurableSchemaTests.cs` runs the emitter against the HR fixture with each of: `schema: "public"` (default), `schema: "hr_prod"`, `schema: "tenant_42"`. For each, asserts every emitted `CREATE TABLE`, every `REFERENCES`, every `CREATE TYPE`, every `CREATE INDEX … ON` statement carries the configured schema qualifier. A fourth case runs with `schema: "1bad"` and asserts: zero output files written, exactly one `PG001` diagnostic with message containing `"not a valid PostgreSQL identifier"` and synthetic target-name span. Closes AC-5.7.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/ConfigurableSchemaTests.cs`.
- **Depends on.** T428.

### T435 [P]. `RelationIndexTests.cs` — btree + GIN choice per cardinality
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/RelationIndexTests.cs` runs against a fixture entity with one cardinality-one relation (`assigned_to: Employee`) and one cardinality-many relation (`tags: Tag*`). Asserts in the emitted `schema/.../Project.sql`:
  - Column `assigned_to_id UUID NOT NULL` plus FK constraint plus `CREATE INDEX IF NOT EXISTS ix_project_assigned_to_id ON … (assigned_to_id);` (btree — no `USING` clause).
  - Column `tags_ids UUID[] NOT NULL DEFAULT '{}'::UUID[]` plus `CREATE INDEX IF NOT EXISTS ix_project_tags_ids ON … USING GIN (tags_ids);`.
  - The two `CREATE INDEX` statements appear in the file sorted ordinally by index name (FR-453): `assigned_to_id` < `tags_ids`.
  
  Closes AC-5.10.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/RelationIndexTests.cs`.
- **Depends on.** T428.

---

## Sub-phase P5c — Goldens + determinism + validity (T460–T469)

Goal: lock the bytes produced by P5b against goldens, validate emitted SQL syntactically (and optionally end-to-end against a real PG instance under a flag), assert determinism across runs and packs. Closes FR-450..FR-455. AC-5.1 (pack), AC-5.3 (samples/registry tree), AC-5.4 (syntax validity), AC-5.5 (idempotency gated), AC-5.9 (determinism). LD-22 (file layout via golden tree).

### T460. Generate goldens — HR registry sample
- **Acceptance.** Developer-driven one-time generation: run the emitter against `samples/registry/` once with default config (`schema: "public"`, `migration_prefix: "V"`) and commit the output under `tests/golden/postgres-ddl/registry/`. The tree must contain:
  - `schema/hr/Employee.sql`, `schema/hr/TimeEntry.sql`, `schema/hr/Project.sql` (3 entities).
  - `schema/hr/ContactInfo.sql` + the 14 result-type composite files (`OnboardResult.sql`, `ActivationResult.sql`, `LeaveResult.sql`, `ReturnResult.sql`, `TerminationResult.sql`, `SubmissionResult.sql`, `ApprovalResult.sql`, `RejectionResult.sql`, `PlanResult.sql`, `StartResult.sql`, `HoldResult.sql`, `ResumeResult.sql`, `CompleteResult.sql`, `CancelResult.sql`).
  - `schema/hr/ContactMethod.sql`, `schema/hr/ContractType.sql` (2 enums).
  - `migrations/hr/V1__Employee.sql`, `migrations/hr/V1__TimeEntry.sql`, `migrations/hr/V1__Project.sql` (3 entity baselines; no migrations for value types or enums because they exist at v1 only).
  
  Hand-review each file: confirm header comment is exactly three lines and carries no timestamp / machine name; column ordering matches FR-452; index ordering matches FR-453; LF line endings only; UTF-8 no BOM; trailing newline. Commit once review passes.
- **Files.** `tests/golden/postgres-ddl/registry/schema/hr/*.sql`, `tests/golden/postgres-ddl/registry/migrations/hr/V1__*.sql`.
- **Depends on.** T428.

### T461. Generate goldens — primitive matrix and multi-version
- **Acceptance.** Generate `tests/golden/postgres-ddl/primitive-matrix/<Primitive><Modifier>.sql` for each (primitive × modifier) combination from T431 (8 × 6 = 48 files). Generate `tests/golden/postgres-ddl/multi-version/schema/x/Employee.v1.sql`, `.v2.sql` and `tests/golden/postgres-ddl/multi-version/migrations/x/V1__Employee.sql`, `V2__Employee.sql` for the AC-5.8 two-version fixture. Hand-review and commit.
- **Files.** `tests/golden/postgres-ddl/primitive-matrix/*.sql`, `tests/golden/postgres-ddl/multi-version/**/*.sql`.
- **Depends on.** T431, T433.

### T462. `GoldenFileTests.cs` — byte-compare + UPDATE_GOLDEN=1
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/GoldenFileTests.cs` mirrors the existing `Gravity.Dsl.Tests/Emitter/JsonSchema/GoldenFileTests.cs` pattern. Two `[Fact]` methods:
  1. `EveryGoldenFile_IsProducedByTheEmitter_AndMatchesByteForByte`: runs the emitter against `samples/registry/`, captures the output buffer via `BufferedEmitterOutput.Snapshot()`, byte-compares each emitted file against the matching golden under `tests/golden/postgres-ddl/registry/`. CRLF→LF normalisation on the golden side to survive Windows checkout.
  2. `EmitterProducesNoExtraFiles_BeyondTheGoldens`: asserts no file in the emitter buffer is absent from the golden tree.
  
  Updating goldens requires `UPDATE_GOLDEN=1` env var (Phase 8 / Phase 9 / Phase 4 convention preserved). Closes AC-5.3.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/GoldenFileTests.cs`.
- **Depends on.** T460, T461.

### T463. `DeterminismTests.cs` — twice-in-process byte identity
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/DeterminismTests.cs` runs the emitter twice in the same process against the same `ResolvedModel` of `samples/registry/`, asserts `BufferedEmitterOutput.Snapshot()` outputs are byte-identical across the two runs (FR-450..FR-455, AC-5.9 first half).
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/DeterminismTests.cs`.
- **Depends on.** T428.

### T464. `PackContentTests.cs` — `.nupkg` shape + pack determinism
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/PackContentTests.cs` runs `dotnet pack Gravity.Dsl.Emitter.PostgresDdl -c Release --output <temp>` and opens the resulting `.nupkg` via `ZipArchive`. Asserts presence of exactly:
  - `lib/net9.0/Gravity.Dsl.Emitter.PostgresDdl.dll`
  - `lib/net9.0/Gravity.Dsl.Emitter.PostgresDdl.xml` (doc file)
  - `buildTransitive/Gravity.Dsl.Emitter.PostgresDdl.props`
  - Standard NuGet `.nuspec` + `[Content_Types].xml`.
  
  Asserts **absence** of: `tasks/` entries, `tools/` entries, `Gravity.Dsl.Compiler.dll`, `Gravity.Dsl.Cli.dll`, `Gravity.Dsl.Emitter.JsonSchema.dll`, `Gravity.Dsl.Emitter.CSharp.dll` (none of those are dependencies of this emitter — FR-400 / FR-463). Marked `[Trait("Category", "Slow")]`. A second pack invocation against the same source tree produces a byte-identical `.nupkg` (compared via SHA-256), closing AC-5.9 pack-determinism half.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/PackContentTests.cs`.
- **Depends on.** T402, T412.

### T465. `SyntaxValidityTests.cs` — every emitted file parses as PG 14 SQL
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/SyntaxValidityTests.cs` runs the emitter against `samples/registry/` and the multi-version fixture from T461. For each emitted `.sql` file, parses through a PG-14-compatible parser. Two implementation options (the test code is written to accept either):
  - **Preferred**: `PgQuery.Net` (or equivalent libpg_query wrapper) as a test-only `PackageReference` with `PrivateAssets="all"`.
  - **Fallback**: shell-out to `psql --version`-gated `psql -d postgres -f <file> --set ON_ERROR_STOP=1 --dry-run` if a PG binary is on the runner PATH; if no binary is available, the test is skipped with an `Assert.Skip("psql not available; install libpg_query or psql to run syntax validation")`.
  
  All emitted files must parse without syntax errors. Marked `[Trait("Category", "Slow")]`. Closes AC-5.4.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/SyntaxValidityTests.cs`, `Directory.Packages.props` (extend with the chosen parser version), `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` (extend with `PrivateAssets="all"` reference).
- **Depends on.** T428.

### T466. `E2EIdempotencyTests.cs` — apply DDL twice, gated by GRAVITY_PG_E2E=1
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/E2EIdempotencyTests.cs` is **gated** by `Environment.GetEnvironmentVariable("GRAVITY_PG_E2E") == "1"`. When the flag is unset (the default for CI lanes that can't pull container images), the test is skipped. When the flag is set: spin up a Postgres 14+ instance via `Testcontainers.PostgreSql` (or `docker run postgres:14`), apply every emitted `schema/` file in path-sort order, then apply them all a second time, then assert `pg_dump --schema-only` output is byte-identical between the two applications. Closes AC-5.5. Marked `[Trait("Category", "E2E")]` for runner-side opt-in.
- **Files.** `Gravity.Dsl.Tests/Emitter/PostgresDdl/E2EIdempotencyTests.cs`, `Directory.Packages.props` (extend with `Testcontainers.PostgreSql` version pin, gated behind `Condition="'$(GRAVITY_PG_E2E)' == '1'"` if the runner supports conditional package references — otherwise plain `PrivateAssets="all"`).
- **Depends on.** T428.

### T467 [P]. CI lane integration
- **Acceptance.** `.github/workflows/ci.yml` (or the equivalent project workflow file) includes the new test assembly's slow lane (PackContentTests, SyntaxValidityTests). The E2E lane (T466) is gated by repository secrets / env so it does not block PR merges; a nightly cron lane may pick it up. The default PR lane runs all non-`[Trait("Category", "E2E")]` tests; this matches the existing Phase 4 / Phase 9c gating model.
- **Files.** `.github/workflows/*.yml` (extend the existing PR workflow with the postgres-ddl test trait include).
- **Depends on.** T462, T464, T465, T466.

### T468 [P]. Docs touch-up
- **Acceptance.** `docs/specs.md` §5.2 reference-emitter row for PostgreSQL DDL gains a one-line annotation pointing at `specs/006-phase-5-postgres-ddl-emitter/spec.md`. `docs/emitter-authoring-guide.md` adds a one-sentence "see also" pointer mentioning the PostgreSQL DDL emitter as the second NuGet-shipped reference emitter after JSON Schema. No content changes to existing sections; this is a pointer-only touch.
- **Files.** `docs/specs.md`, `docs/emitter-authoring-guide.md`.
- **Depends on.** T460.

### T469. End-to-end Final Acceptance gate
- **Acceptance.** Phase 5 is **done** when:
  - `dotnet build -c Release` succeeds with zero warnings.
  - `dotnet test` (default lane, no E2E flag) passes — all of: `RegistrationTests`, `TypeMappingTests`, `AnnotationTests`, `MigrationTests`, `ConfigurableSchemaTests`, `RelationIndexTests`, `PathOrderingTests`, `GoldenFileTests`, `DeterminismTests`, `PackContentTests`, `SyntaxValidityTests` (with parser available), plus the extended `HostIntegrationTests` and `AnnotationNamespaceOwnershipTests`.
  - `dotnet pack Gravity.Dsl.Emitter.PostgresDdl -c Release` succeeds and back-to-back packs produce byte-identical `.nupkg` files.
  - No existing test (any project) regresses.
  - `BannedSymbols.txt` carve-out list unchanged.
  - All FRs / ACs from `spec.md` are closed by at least one task above.
- **Files.** (no new files; this is the acceptance summary)
- **Depends on.** T462, T463, T464, T465, T467, T468.
