# Gravity DSL — Implementation Plan (Phase 5: PostgreSQL DDL reference emitter)

**Status:** Locked for implementation
**Date:** 2026-05-20
**Driven by:** `specs/006-phase-5-postgres-ddl-emitter/spec.md` and `CLAUDE.md` (Principle VI "Pluggable, not prescriptive" dominant; Principle IV gates the migration ledger; Principle II gates the non-goals; "Deterministic output" architectural constraint governs the byte-stability bar).

---

## 1. Strategy

Three sequentially-gated sub-phases. **P5a** scaffolds the new project, lands the `IEmitter` skeleton with the correct target name / annotation namespace / config schema / supported AST range, and wires it into the solution + emitter discovery surface. **P5b** implements every renderer behind the contract — DSL → PG type mapper, identifier mapper, entity-table renderer, value-type composite renderer, enum renderer, lifecycle-state renderer, migration-diff renderer, and the `@postgres` annotation folder. **P5c** locks the bytes against golden files for the HR registry sample (AC-5.3), the primitive matrix (AC-5.6), and a multi-version fixture (AC-5.8); runs the emitter twice to assert determinism (AC-5.9); validates emitted SQL against a PG-14-compatible parser (AC-5.4); optionally exercises idempotency against a real Postgres instance under the integration harness when the runner allows it (AC-5.5).

| Sub-phase | Output | Gate (spec ACs and FRs closed) |
|---|---|---|
| P5a. Project scaffold + contract | New `Gravity.Dsl.Emitter.PostgresDdl/` project (`.csproj`, `PostgresDdlEmitter.cs`, `PostgresDdlEmitterConfig.cs`, `PgRuleIds.cs`, `README.md`, `buildTransitive/.props`). `IEmitter` implementation with `TargetName = "postgres-ddl"`, `AnnotationNamespace = "postgres"`, `SupportedAstVersions = ">=1.0.0 <2.0.0"`. `ConfigurationSchema` declaring `output` (required), `schema` (optional, default `"public"`), `migration_prefix` (optional, default `"V"`). `BannedSymbols.txt` scope unchanged (analyzer remains enabled on the new project). `Gravity.Dsl.sln` extended. | FR-400, FR-401, FR-402, FR-403, FR-404, FR-405, FR-460, FR-461, FR-462, FR-463, FR-464. AC-5.1, AC-5.2. LD-18, LD-19, LD-20. |
| P5b. DDL + migration generation | `Render/TypeMapper.cs` (DSL → PG column type), `Render/Identifier.cs` (snake_case mapping + reserved-word checks), `Render/EntityTableRenderer.cs`, `Render/ValueTypeRenderer.cs`, `Render/EnumRenderer.cs`, `Render/LifecycleStateRenderer.cs`, `Render/MigrationRenderer.cs`, `Render/AnnotationFolder.cs`, `Render/SqlWriter.cs` (deterministic statement formatter). Multi-version `.v<N>.sql` filename rule (FR-425). Per-version migration ledger (FR-426 / FR-427). `PG001..PG004` raised at the appropriate sites. | FR-420..FR-428, FR-430..FR-435, FR-440..FR-443, FR-450..FR-455, FR-470. AC-5.6, AC-5.7, AC-5.8, AC-5.10, AC-5.11, AC-5.12. LD-21, LD-22. |
| P5c. Tests + goldens + integration | `tests/golden/postgres-ddl/registry/` (entity tables + value-type composites + enum types for HR sample). `tests/golden/postgres-ddl/primitive-matrix/` (8 primitives × 6 modifier combos). `tests/golden/postgres-ddl/multi-version/` (two-version fixture per AC-5.8). `Gravity.Dsl.Tests/Emitter/PostgresDdl/{Registration,TypeMapping,Annotation,Migration,GoldenFile,Determinism}Tests.cs`. SQL syntax validation via lightweight PG parser (test-only dependency). Cross-emitter coexistence assertions extending the existing `HostIntegrationTests`. Optional E2E idempotency test gated by `GRAVITY_PG_E2E=1`. | FR-450..FR-455. AC-5.3, AC-5.4, AC-5.5 (gated), AC-5.9. LD-22 (file layout asserted by golden tree). |

## 2. Project layout

Mirrors `Gravity.Dsl.Emitter.JsonSchema/` from Phase 4. The directory tree below shows only the additions; no existing project is modified except `Gravity.Dsl.sln` (new project entry).

```
Gravity.Dsl/
├── Gravity.Dsl.Emitter.PostgresDdl/                       # NEW: NuGet package
│   ├── Gravity.Dsl.Emitter.PostgresDdl.csproj             # NEW: IsPackable=true, net9.0, Apache-2.0
│   ├── PostgresDdlEmitter.cs                              # NEW: IEmitter implementation
│   ├── PostgresDdlEmitterConfig.cs                        # NEW: typed projection of validated EmitterConfig
│   ├── PgRuleIds.cs                                       # NEW: PG001..PG010 rule-id constants
│   ├── Render/
│   │   ├── SqlWriter.cs                                   # NEW: deterministic SQL statement formatter (LF, 4-space indent)
│   │   ├── Identifier.cs                                  # NEW: snake_case mapping + PG identifier validation
│   │   ├── TypeMapper.cs                                  # NEW: DSL primitives / arrays / optionals → PG column type
│   │   ├── AnnotationFolder.cs                            # NEW: @postgres keyword folding + PG003 / PG004
│   │   ├── EntityTableRenderer.cs                         # NEW: CREATE TABLE + indexes + state enum (FR-420, FR-423, FR-424)
│   │   ├── ValueTypeRenderer.cs                           # NEW: CREATE TYPE ... AS (composite) (FR-421)
│   │   ├── EnumRenderer.cs                                # NEW: CREATE TYPE ... AS ENUM (FR-422)
│   │   ├── LifecycleStateRenderer.cs                      # NEW: per-entity <entity>_state enum (FR-424)
│   │   └── MigrationRenderer.cs                           # NEW: V1 baseline + V2+ diffs (FR-426, FR-427)
│   ├── buildTransitive/
│   │   └── Gravity.Dsl.Emitter.PostgresDdl.props          # NEW: contributes DLL to <GravityDslEmitterAssembly>
│   └── README.md                                          # NEW: short "what this package emits" framing
├── tests/golden/postgres-ddl/                             # NEW: byte-checked DDL (AC-5.3, AC-5.6, AC-5.8)
│   ├── registry/
│   │   ├── schema/hr/
│   │   │   ├── Employee.sql
│   │   │   ├── TimeEntry.sql
│   │   │   ├── Project.sql
│   │   │   ├── ContactInfo.sql                            # composite type
│   │   │   ├── ContactMethod.sql                          # enum type
│   │   │   ├── ContractType.sql                           # enum type
│   │   │   ├── OnboardResult.sql, ActivationResult.sql, ... (14 result types as composites)
│   │   └── migrations/hr/
│   │       ├── V1__Employee.sql
│   │       ├── V1__TimeEntry.sql
│   │       └── V1__Project.sql
│   ├── primitive-matrix/                                  # AC-5.6: 8 primitives × 6 modifier combos
│   │   ├── StringPlain.sql, StringOptional.sql, StringArray.sql, StringOptionalArray.sql,
│   │   │  StringArrayOptional.sql, StringOptionalArrayOptional.sql
│   │   ├── Int*.sql, Long*.sql, Decimal*.sql, Boolean*.sql, Date*.sql, DateTime*.sql, UUID*.sql
│   │   └── README.md
│   └── multi-version/                                     # AC-5.8: two-version fixture
│       ├── schema/x/
│       │   ├── Employee.v1.sql
│       │   └── Employee.v2.sql
│       └── migrations/x/
│           ├── V1__Employee.sql
│           └── V2__Employee.sql
└── Gravity.Dsl.Tests/Emitter/PostgresDdl/                 # NEW: test tree mirroring Emitter/JsonSchema/
    ├── RegistrationTests.cs                               # AC-5.2
    ├── TypeMappingTests.cs                                # AC-5.6 (in-code assertions)
    ├── AnnotationTests.cs                                 # AC-5.11, AC-5.12
    ├── MigrationTests.cs                                  # AC-5.8
    ├── GoldenFileTests.cs                                 # AC-5.3, AC-5.6 (byte-checked)
    └── DeterminismTests.cs                                # AC-5.9
```

`Gravity.Dsl.Emitter.PostgresDdl` is a sibling to `Gravity.Dsl.Emitter.JsonSchema` and `Gravity.Dsl.Emitter.CSharp`. It does **not** ship under `samples/`. The two-package distribution model carries through unchanged: consumers add a second `<PackageReference Include="Gravity.Dsl.Emitter.PostgresDdl" Version="..." />` alongside `<PackageReference Include="Gravity.Dsl.MsBuild" Version="..." />`, the `buildTransitive/*.props` wires the emitter assembly into `<GravityDslEmitterAssembly>`, and the existing Phase 9 MSBuild target picks it up by `EmitterRegistry.Discover` at host runtime.

## 3. Module-level architecture

### 3.1 `PostgresDdlEmitter` class shape

`Gravity.Dsl.Emitter.PostgresDdl/PostgresDdlEmitter.cs` is `public sealed class PostgresDdlEmitter : IEmitter` with a public parameterless constructor (FR-405). Its IEmitter surface mirrors `JsonSchemaEmitter.cs` line-for-line in shape so the established pattern is preserved.

```csharp
public sealed class PostgresDdlEmitter : IEmitter
{
    public const string ConfigKeyOutput = "output";
    public const string ConfigKeySchema = "schema";
    public const string ConfigKeyMigrationPrefix = "migration_prefix";
    public const string DefaultSchema = "public";
    public const string DefaultMigrationPrefix = "V";

    internal const string TargetNameValue = "postgres-ddl";

    public string TargetName => TargetNameValue;                          // LD-19 (kebab-case)
    public string AnnotationNamespace => "postgres";                      // LD-19 (identifier — FR-004)
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");                     // FR-401

    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput,           ConfigValueKind.String, Required: true,  Default: null),
        new ConfigKey(ConfigKeySchema,           ConfigValueKind.String, Required: false, Default: DefaultSchema),
        new ConfigKey(ConfigKeyMigrationPrefix,  ConfigValueKind.String, Required: false, Default: DefaultMigrationPrefix)
    ));

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        // 1. Project + pre-flight (FR-402 → PG001 on invalid schema name).
        var typed = PostgresDdlEmitterConfig.From(config, diags);
        if (typed is null) return new EmitResult(diags.ToImmutable());

        // 2. Detect multi-version FQNs (FR-425) and group by FQN to drive
        //    schema/ baseline-of-latest + migrations/ per-version.
        var byFqn = GroupDeclarationsByFqn(model);     // FQN → sorted list of (DeclKey, TopLevelDecl)

        // 3. Map declaration FQN -> SourceFile (identical helper shape to
        //    JsonSchemaEmitter:166-182).
        var declToFile = BuildDeclToFile(model);

        // 4. For each FQN group, emit schema/ files and migrations/ files.
        foreach (var (fqn, versions) in byFqn)
        {
            if (!declToFile.TryGetValue(fqn, out var file)) continue;
            string? ns = file.Namespace?.Name;
            bool multiVersion = versions.Count > 1;

            // 4a. schema/ tree — one file per version (single-version: unqualified;
            //     multi-version: .v<N> for every version).
            foreach (var (key, decl) in versions)
            {
                string body = decl switch
                {
                    EntityDecl e    => EntityTableRenderer.Render(e, model, key, typed, multiVersion, declToFile, diags),
                    ValueTypeDecl v => ValueTypeRenderer.Render(v, model, key, typed, multiVersion, declToFile, diags),
                    EnumDecl en     => EnumRenderer.Render(en, key, typed, multiVersion),
                    _               => string.Empty,
                };
                if (body.Length > 0)
                {
                    sink.WriteFile(SchemaPath(typed, ns, decl, key, multiVersion), body);
                }
            }

            // 4b. migrations/ tree — V1 baseline + V<N> diffs for entities;
            //     value-type / enum migrations only when multi-version.
            MigrationRenderer.RenderAll(versions, model, typed, ns, declToFile, sink, diags);
        }

        return new EmitResult(diags.ToImmutable());
    }
}
```

`BuildDeclToFile`, `ComposeDirectory`, and `Combine` are textual copies of the JSON Schema emitter's helpers; duplication is intentional — the PostgreSQL emitter must not link `Gravity.Dsl.Emitter.JsonSchema` (FR-400 / FR-463 the dependency graph is `Gravity.Dsl.Ast` + `Gravity.Dsl.Emitter` only). All renderer types in `Render/` are `internal sealed` / `internal static`.

### 3.2 `PostgresDdlEmitterConfig`

`PostgresDdlEmitterConfig.cs` is the typed projection. Reuses the `JsonSchemaEmitterConfig.From` shape:

```csharp
internal sealed class PostgresDdlEmitterConfig
{
    public string Output { get; }
    public string Schema { get; }
    public string MigrationPrefix { get; }

    private PostgresDdlEmitterConfig(string output, string schema, string migrationPrefix)
    {
        Output = output;
        Schema = schema;
        MigrationPrefix = migrationPrefix;
    }

    public static PostgresDdlEmitterConfig? From(EmitterConfig config, ImmutableArray<Diagnostic>.Builder diags)
    {
        string output = config.GetString(PostgresDdlEmitter.ConfigKeyOutput);
        string schema = TryGet(config, PostgresDdlEmitter.ConfigKeySchema) ?? PostgresDdlEmitter.DefaultSchema;
        string prefix = TryGet(config, PostgresDdlEmitter.ConfigKeyMigrationPrefix) ?? PostgresDdlEmitter.DefaultMigrationPrefix;

        // FR-470 / PG001 — schema must be a valid PG identifier.
        if (!Identifier.IsValidPgIdentifier(schema))
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                PgRuleIds.Pg001,
                $"schema name '{schema}' is not a valid PostgreSQL identifier (expected [a-z_][a-z0-9_]*, length ≤ 63)",
                new SourceSpan(PostgresDdlEmitter.TargetNameValue, 1, 1, 0)));
            return null;
        }

        return new PostgresDdlEmitterConfig(output, schema, prefix);
    }

    private static string? TryGet(EmitterConfig c, string key)
        => c.Values.TryGetValue(key, out var raw) && raw is string s ? s : null;
}
```

### 3.3 `Render/Identifier.cs`

Pure deterministic helpers, no I/O, no clock:

```csharp
internal static class Identifier
{
    private static readonly Regex PgIdent = new(@"^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);

    public static bool IsValidPgIdentifier(string s)
        => s.Length > 0 && s.Length <= 63 && PgIdent.IsMatch(s);

    /// <summary>FR-451 acronym-aware snake_case mapping.</summary>
    public static string ToSnakeCase(string identifier) { /* see §3.4 */ }

    /// <summary>Quote schema.table.column path for emission; not used in v1 (all names are bare).</summary>
    public static string Qualify(string schema, string name) => $"{ToSnakeCase(schema)}.{ToSnakeCase(name)}";
}
```

### 3.4 Snake-case algorithm (FR-451)

The algorithm is the standard acronym-aware Pascal/Camel → snake_case transformation:

```
foreach char c, with prev:
  if c.IsUpper:
    if prev.IsLower:  emit '_'
    elif prev.IsUpper AND next.IsLower:  emit '_'  // acronym boundary: HTTPSResponse → https_response
    emit ToLower(c)
  else:
    emit c
```

`Identifier.ToSnakeCase("HTTPSResponse")` → `"https_response"`; `Identifier.ToSnakeCase("firstName")` → `"first_name"`; `Identifier.ToSnakeCase("first_name")` → `"first_name"` (idempotent). Unit-tested in T410.

### 3.5 `Render/TypeMapper.cs`

```csharp
internal static class TypeMapper
{
    public static string MapPrimitive(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.String   => "TEXT",
        PrimitiveKind.Int      => "INTEGER",
        PrimitiveKind.Long     => "BIGINT",
        PrimitiveKind.Decimal  => "NUMERIC",
        PrimitiveKind.Boolean  => "BOOLEAN",
        PrimitiveKind.Date     => "DATE",
        PrimitiveKind.DateTime => "TIMESTAMPTZ",
        PrimitiveKind.Uuid     => "UUID",
        _ => throw new NotSupportedException($"primitive {kind} not mapped"),
    };

    /// <summary>FR-432 array/optional composition. Returns the column type string only;
    /// nullability is decided by the caller and appended as " NOT NULL" outside this fn.</summary>
    public static string MapType(TypeRef t, PostgresDdlEmitterConfig cfg, ResolvedModel model, /* ... */)
    {
        // PrimitiveTypeRef → MapPrimitive + [] suffix when IsArray.
        // NamedTypeRef → cfg.Schema + "." + Identifier.ToSnakeCase(name) + [] suffix.
        //   Multi-version: append "_v<N>" to the name when referent is multi-version.
    }
}
```

### 3.6 `Render/EntityTableRenderer.cs`

Composes a full entity file body. Pseudocode:

```
header := "-- Gravity DSL — generated by gravc (target: postgres-ddl)\n"
       + "-- source: " + key.Fqn + " version " + key.Version + "\n"
       + "-- DO NOT EDIT — re-run the emitter to regenerate.\n\n"

stateEnumBlock := LifecycleStateRenderer.Render(entity, cfg, key, multiVersion)
  // DO $$ BEGIN CREATE TYPE <schema>.<entity>_state AS ENUM (...); EXCEPTION WHEN duplicate_object THEN NULL; END $$;

createTable := "CREATE TABLE IF NOT EXISTS " + cfg.Schema + "." + tableName + " (\n"
  + indent(identityColumn) + ",\n"
  + indent("state " + cfg.Schema + "." + stateTypeName + " NOT NULL") + ",\n"
  + indent(joined(propertyColumns + relationColumns)) + ",\n"
  + indent("PRIMARY KEY (" + identityColumnName + ")") + "\n"
  + ");"

alterFkConstraints := sorted(relationOneColumns.Select(r =>
  "ALTER TABLE " + cfg.Schema + "." + tableName + " ADD CONSTRAINT IF NOT EXISTS fk_" + tableName + "_" + relColumnName + " FOREIGN KEY (" + relColumnName + ") REFERENCES " + cfg.Schema + "." + targetTableName + "(" + targetIdentityColumn + ");"))

indexes := sorted(
  relationOneColumns.Select(r => "CREATE INDEX IF NOT EXISTS ix_" + tableName + "_" + relColumnName + " ON " + cfg.Schema + "." + tableName + "(" + relColumnName + ");")
  ++ relationManyColumns.Select(r => "CREATE INDEX IF NOT EXISTS ix_" + tableName + "_" + relColumnName + " ON " + cfg.Schema + "." + tableName + " USING GIN (" + relColumnName + ");")
  ++ annotationIndexedColumns.Select(c => "CREATE INDEX IF NOT EXISTS ix_" + tableName + "_" + c + " ON " + cfg.Schema + "." + tableName + "(" + c + ");"))

body := header + stateEnumBlock + "\n\n" + createTable + "\n\n" + alterFkConstraints + "\n\n" + indexes + "\n"
```

PG14 note: `ADD CONSTRAINT IF NOT EXISTS` is not native PG syntax — we use a `DO $$ ... EXCEPTION WHEN duplicate_object THEN NULL; END $$` envelope for foreign-key constraints just like for `CREATE TYPE`. The pseudocode above elides this detail; the actual renderer (T423) wraps each FK in the envelope.

### 3.7 `Render/MigrationRenderer.cs`

```csharp
internal static class MigrationRenderer
{
    public static void RenderAll(
        IReadOnlyList<(DeclKey Key, TopLevelDecl Decl)> versions,
        ResolvedModel model,
        PostgresDdlEmitterConfig cfg,
        string? namespaceName,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IEmitterOutput sink,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        // For entities: emit V1 baseline (full CREATE TABLE) + V<N> diffs for N >= 2.
        // For value types / enums: emit migrations ONLY when versions.Count > 1.
        //   V1 baseline = full CREATE TYPE; V<N> diffs = ALTER TYPE ADD ATTRIBUTE / ADD VALUE.
        //
        // Diff computation between consecutive versions:
        //   newProps      = vN.Properties.Except(vN-1.Properties, byName)
        //   newRelations  = vN.Relations.Except(vN-1.Relations, byName)
        //   newStates     = vN.Lifecycle.States.Except(vN-1.Lifecycle.States, ordinal)
        // Emit:
        //   ALTER TABLE ... ADD COLUMN IF NOT EXISTS <col> <type> [NOT NULL] ...; per new prop / relation
        //   ALTER TYPE   ... ADD VALUE IF NOT EXISTS '<state>';                  per new state
        //   CREATE INDEX IF NOT EXISTS ix_<table>_<col>_id ON ...;               per new relation-one
        //   CREATE INDEX IF NOT EXISTS ix_<table>_<col>_ids ON ... USING GIN;    per new relation-many
        // No DROP, no ALTER COLUMN TYPE — Principle IV guarantees they aren't needed.
    }
}
```

The diff computation is **structural**, not semantic — we compare AST records by name and rely on the additive-only rule guaranteed by the Phase 8 breaking-change validator (`VAL020..VAL030`) upstream. The emitter does **not** re-check whether the diff is actually additive; if a model with non-additive evolution reaches this renderer, the upstream validator would have already rejected it.

### 3.8 `Render/AnnotationFolder.cs`

```csharp
internal static class AnnotationFolder
{
    public sealed record FoldedAttributes(
        string? OverrideColumnName,
        bool MarkUnique,
        bool MarkIndexed,
        string? DefaultExpression);

    public static FoldedAttributes Fold(
        ImmutableArray<AnnotationDecl> annotations,
        string propertyName,
        string entityFqn,
        ImmutableArray<Diagnostic>.Builder diags) { /* … */ }
}
```

`Fold` walks `@postgres(...)` annotations:
- `column` → `OverrideColumnName` (string; if invalid PG identifier, emit `PG004`).
- `unique` → `MarkUnique` (boolean).
- `index` → `MarkIndexed` (boolean).
- `default` → `DefaultExpression` (string, emitted verbatim).
- **Reserved keys** (`precision`, `scale`, `max_length`, `storage`, `index_method`, `partial_where`) → emit `PG003` ("reserved for future use; not consumed in v1").
- **Unknown keys** → emit `PG003`.
- **Wrong value type** for any claimed key → emit `PG003`.

### 3.9 `Render/SqlWriter.cs`

Tiny helper module: 4-space indentation constant, LF line ending constant, `Combine` for path assembly, escape function for SQL string literals (single quotes doubled for enum variants and default expressions — though defaults are emitted verbatim per FR-441 so no escaping is performed on user-authored default strings). Provides one entrypoint `Compose(string header, params string[] blocks)` that joins blocks with `"\n\n"` and ensures the result ends with `"\n"`.

### 3.10 `Render/LifecycleStateRenderer.cs`

```csharp
internal static class LifecycleStateRenderer
{
    public static string Render(EntityDecl entity, PostgresDdlEmitterConfig cfg, DeclKey key, bool multiVersion)
    {
        string tableName = TableName(entity, key, multiVersion);     // employee or employee_v2
        string typeName = tableName + "_state";
        var variants = string.Join(",\n        ", entity.Lifecycle.States.Select(s => $"'{s}'"));
        return $"""
            DO $$
            BEGIN
                CREATE TYPE {cfg.Schema}.{typeName} AS ENUM (
                    {variants}
                );
            EXCEPTION
                WHEN duplicate_object THEN NULL;
            END $$;
            """;
    }
}
```

(Pseudocode — actual indentation matches FR-454's 4-space rule and ends with no trailing whitespace.)

### 3.11 Multi-version filename convention

`TableName` = single-version → `Identifier.ToSnakeCase(entity.Name)`; multi-version → `Identifier.ToSnakeCase(entity.Name) + "_v" + key.Version.ToString(CultureInfo.InvariantCulture)`. Schema-file paths apply the `.v<N>.sql` infix per FR-425.

The `_v<N>` suffix on table names is **only** for the multi-version case. The single-version path keeps the unqualified table name (`employee`, not `employee_v1`), matching the JSON Schema emitter's filename rule.

### 3.12 Diagnostics

`PgRuleIds.cs` declares constants for `PG001..PG010`. Diagnostic emission uses `new Diagnostic(severity, ruleId, message, span)` directly. Spans:
- Per-property: `PropertyDecl.Span`.
- Per-annotation: `AnnotationDecl.Span`.
- Config-block (schema name invalid): synthetic span with file = `TargetName` value, line/col = 1/1.

## 4. Dependencies and packaging

| Concern | Setting |
|---|---|
| TargetFramework | `net9.0` |
| Determinism | `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, `<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>` |
| Packing | `<IsPackable>true</IsPackable>`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>` |
| License | `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` |
| Dependencies | ProjectReferences: `Gravity.Dsl.Ast`, `Gravity.Dsl.Emitter`. No third-party PackageReferences. |
| Banned APIs | Default scope — analyzer remains enabled; no carve-out in `Directory.Build.props`. |
| Build-target wiring | `buildTransitive/Gravity.Dsl.Emitter.PostgresDdl.props` contributes the assembly path to `<GravityDslEmitterAssembly>`, mirroring the JSON Schema emitter's `.props` shape. |

## 5. Risk register

| Risk | Mitigation |
|---|---|
| **R1**: `CREATE TYPE` lacks native `IF NOT EXISTS` — naive idempotency breaks. | Wrap every `CREATE TYPE` in `DO $$ BEGIN ... EXCEPTION WHEN duplicate_object THEN NULL; END $$` (LD-21). Asserted by AC-5.5 idempotency test (gated). |
| **R2**: `ALTER TABLE ... ADD CONSTRAINT` lacks native `IF NOT EXISTS`. | Same envelope as R1 for FK constraints. Documented in §3.6. |
| **R3**: Snake-case mapping for mixed-acronym identifiers is ambiguous. | Lock the algorithm in FR-451 with worked examples; unit-test all corner cases in T410. |
| **R4**: Cardinality-many encoded as PG array drops referential integrity. | Documented in FR-434 — PG does not natively enforce FKs on array elements; the operator may layer a constraint trigger downstream. Single-source-of-truth call documented in NG-10 / NG-11. |
| **R5**: Multi-version coexistence in PG (composite-type versioning, separate table per version) is uncommon and may surprise operators. | Documented in LD-22 / FR-425. The single-version path is the dominant case; multi-version is exercised by a focused fixture (AC-5.8) but not by the HR registry sample. |
| **R6**: SQL syntax validation in tests requires either a real PG instance or a parser dependency. | Default to a lightweight `Antlr4`-based PG parser (test-only) per AC-5.4. The full-database E2E idempotency check (AC-5.5) is gated by `GRAVITY_PG_E2E=1` so CI can skip it on runners that can't pull container images. |
| **R7**: `@postgres(default: "...")` accepts an unsanitised string — SQL injection risk. | The emitter trusts the user-authored default (G-8 rationale: deterministic because user-authored, never invented by emitter). The user is opting into a PG expression; the same trust model applies that PG itself applies — the string is evaluated by the database server when the migration runs. Operators who want validation can use a downstream linter. |
| **R8**: Determinism failures from `string.Join` culture-sensitivity. | All `ToString` calls pass `CultureInfo.InvariantCulture`; no `string.Compare` without `StringComparison`; banned-APIs analyzer catches regressions. |

## 6. Out-of-band parallelism

P5b renderer tasks (T420..T428) are mutually independent and can land in parallel. P5c tests can land in parallel against the locked P5b output. P5a tasks have one critical-path chain (`T400 → T403 → T404`); other P5a tasks (`T405..T412`) can be parallelised.

## 7. Acceptance check before P5c → ship

- [ ] All FRs in the spec map to tasks below.
- [ ] All ACs in the spec map to test tasks below.
- [ ] `dotnet build -c Release` succeeds with zero warnings.
- [ ] `dotnet test` passes (all suites — emitter, host integration, cross-emitter coexistence).
- [ ] `dotnet pack Gravity.Dsl.Emitter.PostgresDdl -c Release` produces a byte-identical `.nupkg` on back-to-back runs.
- [ ] Two emitter runs against `samples/registry/` produce byte-identical output buffers.
- [ ] No new dependency introduced into the emitter assembly's runtime closure (BCL only).
- [ ] `BannedSymbols.txt` carve-out list unchanged; the emitter project remains under analyzer scope.
