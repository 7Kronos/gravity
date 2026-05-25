# Gravity DSL — Implementation Plan (Phase 4: JSON Schema reference emitter)

**Status:** Locked for implementation
**Date:** 2026-05-18
**Driven by:** `specs/004-phase-4-json-schema-emitter/spec.md` and `CLAUDE.md` (Principle VI "Pluggable, not prescriptive" dominant; Principle I gates artifact-as-generated; Principle III governs `additionalProperties: false`; "Deterministic output" architectural constraint governs serialisation strategy).

---

## 1. Strategy

Three scoped sub-phases executed sequentially. P4a scaffolds the new project, registers the emitter against the existing `EmitterRegistry` discovery contract, and lands the configuration schema; P4b implements every renderer driving every shape declared in LD-15 (entity-state root, per-event payload, per-command request, per-command response, per-state enum, per-value-type file, per-enum file) plus the type-mapping table (FR-330..FR-334) and `@json_schema` annotation folding (FR-340..FR-344); P4c covers golden-file lock-in, validator-side metaschema round-tripping, cross-emitter integration, and determinism. The three halves are sequentially gated: P4b cannot begin until the IEmitter surface compiles and is discoverable through `EmitterRegistry.FromInstances`; P4c cannot begin until P4b's renderer set produces a complete file tree for `samples/registry/`. Phase 4 is strictly additive at the new-project layer — no existing AST records, resolver, validator, emitter host, CLI, or MSBuild code is modified (spec §6 "Cross-references", final paragraph).

| Sub-phase | Output | Gate (spec ACs and FRs closed) |
|---|---|---|
| P4a. Project scaffold + contract | New `Gravity.Dsl.Emitter.JsonSchema/` project (`.csproj`, `JsonSchemaEmitter.cs`, `JsonSchemaEmitterConfig.cs`, `JsonRuleIds.cs`, `README.md`). `IEmitter` implementation with `TargetName = "json-schema"`, `AnnotationNamespace = "json_schema"`, `SupportedAstVersions = ">=1.0.0 <2.0.0"`. `ConfigurationSchema` declaring `output` (required) and `bundle_strategy` (optional, default `"per-entity"`). `EmitterRegistry` discovery via assembly scan. `BannedSymbols.txt` scope unchanged (analyzer remains enabled on the new project). `Gravity.Dsl.sln` extended. | FR-300, FR-301, FR-302, FR-303, FR-304, FR-305, FR-360, FR-361, FR-362, FR-363, FR-364. AC-4.1, AC-4.2. LD-14, LD-17. |
| P4b. Schema generation | `Render/EntityBundleRenderer.cs`, `Render/EventPayloadRenderer.cs`, `Render/CommandReqRespRenderer.cs`, `Render/ValueTypeRenderer.cs`, `Render/EnumRenderer.cs`, `Render/LifecycleStateRenderer.cs`, `Render/PropertyRenderer.cs`. `TypeMapper.cs` (DSL → JSON Schema fragment). `AnnotationFolder.cs` (claimed-keyword folding with per-key value-type contracts). `SortedKeyJsonWriter.cs` (deterministic emission helper). Multi-version `.v<N>.json` filename rule (FR-310 / FR-316 / FR-317). Cross-file `$ref` resolution via FQN walking the resolved model (FR-318). `JS001..JS004` raised at the appropriate sites. | FR-310, FR-311, FR-312, FR-313, FR-314, FR-315, FR-316, FR-317, FR-318, FR-319, FR-330, FR-331, FR-332, FR-333, FR-334, FR-340, FR-341, FR-342, FR-343, FR-344, FR-365. AC-4.3, AC-4.5, AC-4.6, AC-4.7, AC-4.8, AC-4.12. LD-15, LD-16. |
| P4c. Tests + goldens + integration | `tests/golden/json-schema/registry/` (3 entity bundles + 15 value-type files (1 ContactInfo + 14 result types) + 2 enum files = 20 byte-checked files). `tests/golden/json-schema/primitive-matrix/` (8 primitives × 4 modifier combos). `tests/fixtures/json-schema/instances/` (one valid sample instance per entity). `Gravity.Dsl.Tests/Emitter/JsonSchema/{Registration,TypeMapping,AnnotationFolding,SchemaValidity,GoldenFile,Determinism}Tests.cs`. `JsonSchema.Net` added as test-project-only dependency (`PrivateAssets="all"`). Cross-emitter `HostIntegrationTests` extension to assert co-existence with `csharp` and `outline`. | FR-350, FR-351, FR-352, FR-353, FR-354, FR-355. AC-4.4, AC-4.9, AC-4.10, AC-4.11. |

Phases 5–7 (GraphQL SDL, OpenAPI, AsyncAPI reference emitters), the proposed `--draft 2020-12` multi-dialect surface (Phase 10+), absolute `$id` URIs, version-segmented `$id`, schema migration / diff tooling, auto-generation of TypeScript / Python types from JSON Schema, and runtime validator selection are referenced but not addressed; per spec NG-1..NG-11 they remain future work against the Phase 4-shipped emitter surface.

## 2. Project layout

Mirrors the predecessor plan; restricted to additions required by Phase 4. No existing project is modified except `Gravity.Dsl.sln` (new project entry), `Directory.Packages.props` (`JsonSchema.Net` test-only version pin), and `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` (one new `<PackageReference Include="JsonSchema.Net" PrivateAssets="all" />`). The directory tree below shows only the additions.

```
Gravity.Dsl/
├── Gravity.Dsl.Emitter.JsonSchema/                # NEW: NuGet package
│   ├── Gravity.Dsl.Emitter.JsonSchema.csproj      # NEW: IsPackable=true, net9.0, Apache-2.0
│   ├── JsonSchemaEmitter.cs                       # NEW: IEmitter implementation
│   ├── JsonSchemaEmitterConfig.cs                 # NEW: typed view of the validated EmitterConfig
│   ├── JsonRuleIds.cs                             # NEW: JS001..JS010 rule-id constants
│   ├── SortedKeyJsonWriter.cs                     # NEW: deterministic System.Text.Json wrapper
│   ├── TypeMapper.cs                              # NEW: DSL primitives / arrays / optionals → fragment
│   ├── AnnotationFolder.cs                        # NEW: @json_schema keyword folding + JS001 / JS002 / JS004
│   ├── Render/
│   │   ├── EntityBundleRenderer.cs                # NEW: per-entity root document (FR-310, FR-314, FR-319)
│   │   ├── EventPayloadRenderer.cs                # NEW: definitions entries for events (FR-311)
│   │   ├── CommandReqRespRenderer.cs              # NEW: definitions entries for command request + response (FR-312, FR-313)
│   │   ├── ValueTypeRenderer.cs                   # NEW: namespace-scope value type files (FR-316)
│   │   ├── EnumRenderer.cs                        # NEW: namespace-scope enum files (FR-317)
│   │   ├── LifecycleStateRenderer.cs              # NEW: definitions/<EntityName>State (FR-315)
│   │   └── PropertyRenderer.cs                    # NEW: shared property → schema fragment + annotation fold
│   └── README.md                                  # NEW: short "what this package emits" framing
├── tests/golden/json-schema/                      # NEW: byte-checked schemas (AC-4.5, AC-4.11)
│   ├── registry/
│   │   └── hr/
│   │       ├── Employee.json
│   │       ├── TimeEntry.json
│   │       ├── Project.json
│   │       ├── ContactInfo.json                   # value type, namespace scope
│   │       ├── ContactMethod.json                 # enum, namespace scope
│   │       ├── ContractType.json                  # enum, namespace scope
│   │       ├── OnboardResult.json                 # ... 15 value types total (1 ContactInfo + 14 result types)
│   │       ├── ActivationResult.json
│   │       ├── LeaveResult.json
│   │       ├── ReturnResult.json
│   │       ├── TerminationResult.json
│   │       ├── SubmissionResult.json
│   │       ├── ApprovalResult.json
│   │       ├── RejectionResult.json
│   │       ├── PlanResult.json
│   │       ├── StartResult.json
│   │       ├── HoldResult.json
│   │       ├── ResumeResult.json
│   │       ├── CompleteResult.json
│   │       └── CancelResult.json
│   └── primitive-matrix/                          # AC-4.5: 8 primitives × 4 modifier combos
│       ├── StringPlain.json, StringOptional.json, StringArray.json, StringOptionalArray.json
│       ├── Int*.json, Long*.json, Decimal*.json, Boolean*.json
│       ├── Date*.json, DateTime*.json, UUID*.json
│       └── README.md                              # documents the parameterised mapping
├── tests/fixtures/json-schema/instances/          # NEW: hand-authored minimal valid instances (AC-4.4)
│   ├── Employee.instance.json
│   ├── TimeEntry.instance.json
│   └── Project.instance.json
└── Gravity.Dsl.Tests/Emitter/JsonSchema/          # NEW: test tree mirroring Emitter/CSharp/
    ├── RegistrationTests.cs                       # AC-4.2: TargetName / AnnotationNamespace / SupportedAstVersions
    ├── TypeMappingTests.cs                        # AC-4.5: parameterised primitive matrix
    ├── AnnotationFoldingTests.cs                  # AC-4.8, AC-4.12: claimed-keyword folding + JS001 / JS004
    ├── SchemaValidityTests.cs                     # AC-4.4: every emitted file validates against the Draft-07 metaschema
    ├── GoldenFileTests.cs                         # AC-4.11: byte-checked golden tree (mirrors CSharp/GoldenFileTests.cs)
    └── DeterminismTests.cs                        # AC-4.9: twice-in-process byte identity
```

`Gravity.Dsl.Emitter.JsonSchema` is sibling to `Gravity.Dsl.Emitter.CSharp` and `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline`. It does NOT ship under `samples/` — it is a first-party reference emitter, not a sample (LD-16 § final sentence). The two-package distribution model from Phase 9 carries through: consumers add a second `<PackageReference Include="Gravity.Dsl.Emitter.JsonSchema" Version="..." />` alongside `<PackageReference Include="Gravity.Dsl.MsBuild" Version="..." />`, the `buildTransitive/*.props` (§3.8) wires the emitter assembly into the shared `<GravityDslEmitterAssembly>` item, and the existing Phase 9 MSBuild target picks it up by `EmitterRegistry.Discover` at host runtime.

## 3. Module-level architecture

### 3.1 `JsonSchemaEmitter` class shape

`Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitter.cs` is `public sealed class JsonSchemaEmitter : IEmitter` with a public parameterless constructor (FR-305 — the `EmitterRegistry.AppendEmittersFromAssembly` discovery contract requires both). Its IEmitter surface and `Emit` body mirror `CSharpEmitter.cs` line-for-line in shape so a community author transitioning from "read C# emitter, write my own" recognises the idiom.

```csharp
public sealed class JsonSchemaEmitter : IEmitter
{
    public const string ConfigKeyOutput = "output";
    public const string ConfigKeyBundleStrategy = "bundle_strategy";
    public const string DefaultBundleStrategy = "per-entity";

    public string TargetName => "json-schema";                         // LD-17 (kebab-case)
    public string AnnotationNamespace => "json_schema";                // LD-17 (underscore — identifier grammar)
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");                  // FR-301 (Phase 0–3 1.0.0 + Phase 8 1.1.0)

    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput, ConfigValueKind.String, Required: true, Default: null),
        new ConfigKey(ConfigKeyBundleStrategy, ConfigValueKind.String, Required: false, Default: DefaultBundleStrategy)
    ));

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        // 1. Project the validated config + pre-flight bundle_strategy (FR-302 → JS002).
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var typed = JsonSchemaEmitterConfig.From(config, diags);
        if (typed is null) return new EmitResult(diags.ToImmutable());

        // 2. Detect multi-version FQN coexistence — used by FR-310 .v<N>.json filename rule and
        //    by FR-318 $ref resolution. One pass over Declarations producing a HashSet<string> of
        //    FQNs that have more than one Version in scope.
        var multiVersionFqns = ComputeMultiVersionFqns(model);

        // 3. Map declaration FQN -> SourceFile (identical helper shape to CSharpEmitter:105–122).
        var declToFile = BuildDeclToFile(model);

        // 4. Walk Declarations in (FQN ordinal, Version asc) order — FR-303 / the FR-161 contract
        //    pinned by ResolvedModel.Declarations being an ImmutableSortedDictionary.
        foreach (var kv in model.Declarations)
        {
            var sourceFile = declToFile[kv.Key.Fqn];
            string? dslNs = sourceFile.Namespace?.Name;
            string dir = Combine(typed.Output, ComposeDirectory(dslNs));
            int version = kv.Key.Version;
            bool versioned = multiVersionFqns.Contains(kv.Key.Fqn);

            switch (kv.Value)
            {
                case EntityDecl entity:
                    var bundle = EntityBundleRenderer.Render(entity, model, kv.Key, multiVersionFqns, diags);
                    sink.WriteFile(BundleFilename(dir, entity.Name, version, versioned), bundle);
                    break;
                case ValueTypeDecl vt:
                    var vtSchema = ValueTypeRenderer.Render(vt, model, kv.Key, multiVersionFqns, diags);
                    sink.WriteFile(BundleFilename(dir, vt.Name, version, versioned), vtSchema);
                    break;
                case EnumDecl en:
                    var enSchema = EnumRenderer.Render(en, kv.Key, multiVersionFqns);
                    sink.WriteFile(BundleFilename(dir, en.Name, version, versioned), enSchema);
                    break;
            }
        }

        return new EmitResult(diags.ToImmutable());
    }

    private static string BundleFilename(string dir, string name, int version, bool versioned)
    {
        // FR-310 multi-version case: .v<N>.json infix applies to EVERY version when more than one
        // version of the FQN is in scope, including version 1 (symmetric layout, no off-by-one).
        string file = versioned ? $"{name}.v{version}.json" : $"{name}.json";
        return Combine(dir, file).Replace('\\', '/');  // BufferedEmitterOutput normalises but be explicit
    }
}
```

`BuildDeclToFile` and `ComposeDirectory` are textual copies of the C# emitter's helpers (CSharpEmitter.cs:105–122 and `NamespaceMapper.ComposeDirectory`); duplication is intentional — the JSON Schema emitter must not link `Gravity.Dsl.Emitter.CSharp` (per FR-300 / FR-363 the dependency graph is `Gravity.Dsl.Ast` + `Gravity.Dsl.Emitter` only, plus the BCL `System.Text.Json`). All renderer helper types in `Render/` are `internal sealed`, locking the public surface to `JsonSchemaEmitter` + `JsonSchemaEmitterConfig` + `JsonRuleIds` only.

### 3.2 `JsonSchemaEmitterConfig`

`Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitterConfig.cs` is the typed projection of the host-validated `EmitterConfig`. Mirrors the shape of `OutlineEmitterConfig.From`:

```csharp
internal sealed class JsonSchemaEmitterConfig
{
    public string Output { get; }
    public string BundleStrategy { get; }

    private JsonSchemaEmitterConfig(string output, string bundleStrategy)
    {
        Output = output;
        BundleStrategy = bundleStrategy;
    }

    /// <summary>
    /// Project + pre-flight. Returns null and appends JS002 to <paramref name="diags"/> when
    /// bundle_strategy is set to a value other than "per-entity" (FR-302, FR-364).
    /// </summary>
    public static JsonSchemaEmitterConfig? From(EmitterConfig config, ImmutableArray<Diagnostic>.Builder diags)
    {
        var output = (string)config.Values[JsonSchemaEmitter.ConfigKeyOutput];
        var strategy = config.Values.TryGetValue(JsonSchemaEmitter.ConfigKeyBundleStrategy, out var raw)
            ? (string)raw
            : JsonSchemaEmitter.DefaultBundleStrategy;
        if (!string.Equals(strategy, JsonSchemaEmitter.DefaultBundleStrategy, StringComparison.Ordinal))
        {
            diags.Add(Diagnostic.Error(
                ruleId: JsonRuleIds.Js002,
                message: $"bundle_strategy '{strategy}' is not recognised; the only legal value in v1 is 'per-entity'",
                span: SyntheticSpanForTarget("json-schema")));
            return null;
        }
        return new JsonSchemaEmitterConfig(output, strategy);
    }
}
```

The synthetic span pattern (target-name-only `SourceSpan`) reuses the same idiom as `HOST001` / `HOST002`; the emitter does not invent a new span shape. `output` is `Required: true`, so the host has already raised the appropriate `HOST` diagnostic if it is absent — the typed view does not re-check.

### 3.3 `JsonRuleIds`

`Gravity.Dsl.Emitter.JsonSchema/JsonRuleIds.cs`:

```csharp
internal static class JsonRuleIds
{
    public const string Js001 = "JS001";   // unknown @json_schema key OR annotation value type mismatch
                                           // OR multipleOf: 0 (FR-341, FR-342, FR-364). Severity Error.
    public const string Js002 = "JS002";   // bundle_strategy set to a value other than "per-entity"
                                           // (FR-302, FR-364). Severity Error.
    public const string Js003 = "JS003";   // user property name collides with reserved 'state' on
                                           // the entity-state schema (FR-315, FR-364). Severity Error.
    public const string Js004 = "JS004";   // @json_schema(format: "<value>") carries a format string
                                           // not in the emitter's known set (FR-341, FR-364). Severity Warning.
    public const string Js005 = "JS005";   // reserved
    public const string Js006 = "JS006";   // reserved
    public const string Js007 = "JS007";   // reserved
    public const string Js008 = "JS008";   // reserved
    public const string Js009 = "JS009";   // reserved
    public const string Js010 = "JS010";   // reserved
}
```

`JS*` constants ship with the emitter assembly only — they are NOT added to the compiler library's `RuleIds.cs` (same separation pattern Phase 9 established for `MSB*`). A third-party emitter that links `Gravity.Dsl.Emitter` does not see (or accidentally redefine) these ids.

### 3.4 Renderers

Every renderer is `internal static class` and exposes a single `Render(...)` method returning a serialised UTF-8 string with LF line endings. Renderers do NOT write to the sink directly — they hand back the body string and `JsonSchemaEmitter.Emit` issues the `IEmitterOutput.WriteFile` call. This is the same separation `CSharpEmitter` uses (Renderers.cs returns strings; CSharpEmitter.cs:98–103 calls `sink.WriteFile`).

#### 3.4.1 `EntityBundleRenderer.Render`

```csharp
internal static class EntityBundleRenderer
{
    private const string Draft07Uri = "http://json-schema.org/draft-07/schema#";

    public static string Render(
        EntityDecl entity,
        ResolvedModel model,
        DeclKey key,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        // 1. Build the entity-state properties map in DSL declaration order (FR-314, FR-352):
        //    a. identity field, b. each PropertyDecl, c. each RelationDecl (FR-314 step 3).
        //    The 'state' slot is added LAST in declaration order but appears in canonical key
        //    order at write time — see SortedKeyJsonWriter (§3.7).
        var props = new List<(string Key, JsonNode Value, bool Required)>();
        props.Add((entity.Identity.FieldName, TypeMapper.MapPrimitive(entity.Identity.Type), Required: true));
        foreach (var p in entity.Properties)
        {
            // JS003: user property collides with reserved 'state' (FR-315).
            if (string.Equals(p.Name, "state", StringComparison.Ordinal))
            {
                diags.Add(Diagnostic.Error(JsonRuleIds.Js003,
                    $"entity '{key.Fqn}' declares a property named 'state' that collides with the reserved entity-state property",
                    p.Span));
                continue;
            }
            var fragment = PropertyRenderer.Render(p.Name, p.Type, p.Annotations, model, multiVersionFqns, diags);
            props.Add((p.Name, fragment, Required: !p.Type.IsOptional));
        }
        foreach (var r in entity.Relations)
        {
            var (relName, relFragment) = TypeMapper.MapEntityRelation(r);
            props.Add((relName, relFragment, Required: true));
        }
        // The 'state' slot: $ref to #/definitions/<EntityName>State (FR-315, always required).
        props.Add(("state", new JsonObject { ["$ref"] = $"#/definitions/{entity.Name}State" }, Required: true));

        // 2. Build the definitions map: events, command requests, command responses, the state enum,
        //    plus any future entity-internal value types/enums (none in v1 grammar). Keys are
        //    sorted ordinally at write time per FR-355.
        var defs = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var e in entity.Events)
            defs[e.Name] = EventPayloadRenderer.Render(e, key.Fqn, model, multiVersionFqns, diags);
        foreach (var c in entity.Commands)
        {
            defs[$"{c.Name}Request"] = CommandReqRespRenderer.RenderRequest(c, key.Fqn, model, multiVersionFqns, diags);
            defs[$"{c.Name}Response"] = CommandReqRespRenderer.RenderResponse(c, model, multiVersionFqns);
        }
        defs[$"{entity.Name}State"] = LifecycleStateRenderer.Render(entity.Lifecycle);

        // 3. Compose root document. SortedKeyJsonWriter (§3.7) enforces FR-351 canonical key order.
        var root = new JsonObject
        {
            ["$schema"] = Draft07Uri,
            ["title"] = key.Fqn,
            ["x-gravity-version"] = key.Version,
            ["type"] = "object",
            ["properties"] = BuildPropertiesMap(props),                        // declaration order (FR-352)
            ["required"] = BuildRequiredArray(props),                          // sorted ordinal (FR-353)
            ["additionalProperties"] = false,                                  // FR-319
            ["definitions"] = BuildDefsMap(defs)                                     // sorted ordinal by key (FR-355)
        };
        return SortedKeyJsonWriter.Serialize(root);
    }
}
```

#### 3.4.2 `EventPayloadRenderer.Render`

`Render(EventDecl ev, string entityFqn, ResolvedModel model, IReadOnlySet<string> mvFqns, diags)` produces:

```json
{
  "title": "<EntityFQN>.<EventName>",
  "type": "object",
  "properties": { /* fields in DSL declaration order */ },
  "required": [ /* non-optional field names, sorted ordinally */ ],
  "additionalProperties": false
}
```

Empty payload (`EventName {};`) collapses cleanly: `"properties": {}, "required": []`. The `definitions` key is the bare event name (e.g. `Submitted`), not `Events.Submitted` (FR-311).

#### 3.4.3 `CommandReqRespRenderer`

Two methods:

- `RenderRequest(CommandDecl cmd, string entityFqn, model, mvFqns, diags)` → `{ "title": "<EntityFQN>.<CommandName>.Request", "type": "object", "properties": { /* arguments */ }, "required": [...], "additionalProperties": false }`. Zero-argument commands collapse to the empty form (FR-312).
- `RenderResponse(CommandDecl cmd, model, mvFqns)` → always a `$ref` indirection. `cmd.ReturnsType` is a bare identifier (Phase 8 FR-100 forbids `@N` suffix on `returns`). When the response type resolves to an entity-local definition (currently never in v1 grammar; reserved): `{ "$ref": "#/definitions/<ReturnsType>" }`. When it resolves to a namespace-scope `ValueTypeDecl` or `EnumDecl` (the common case): `{ "$ref": "<ReturnsType>.json" }` — same-namespace sibling. Cross-namespace references use the namespace-qualified relative path machinery from `TypeMapper.MapNamedType` (§3.5) when the resolver places the target under a different namespace. The indirection rule means switching the response type's declaration location is a single-line $ref change, not a deep restructuring (FR-313).

#### 3.4.4 `ValueTypeRenderer.Render`

`Render(ValueTypeDecl vt, ResolvedModel model, DeclKey key, mvFqns, diags)` produces a stand-alone file:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "<TypeFQN>",
  "x-gravity-version": <int>,
  "type": "object",
  "properties": { /* fields in DSL declaration order */ },
  "required": [ /* non-optional field names, sorted ordinally */ ],
  "additionalProperties": false
}
```

The file does NOT embed dependencies — cross-references to other value types or enums go through `$ref` to a sibling file (FR-316, FR-318). Annotation folding on a field flows through `PropertyRenderer.Render` so the same `@json_schema` keywords claimed for entity properties apply to value-type fields.

#### 3.4.5 `EnumRenderer.Render`

`Render(EnumDecl en, DeclKey key, mvFqns)` produces:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "<EnumFQN>",
  "x-gravity-version": <int>,
  "type": "string",
  "enum": [ /* variants in DSL declaration order — NOT sorted */ ]
}
```

Variant ordering is declaration order (FR-317, FR-354) — the asymmetry with `required` (which IS sorted, FR-353) is intentional: enums carry semantic ordering (the first lifecycle state is implicitly initial per FR-033 from Phase 0–3); `required` arrays do not. No `additionalProperties` keyword on string schemas — the `enum` constraint already closes the value space (FR-319 third sentence).

#### 3.4.6 `LifecycleStateRenderer.Render`

`Render(LifecycleDecl lc)` produces `{ "type": "string", "enum": [<states in DSL declaration order>] }`. This is the body of the `definitions/<EntityName>State` entry inside the entity bundle. The Phase 0–3 grammar guarantees at least one state per entity (FR-024); a hypothetical zero-state entity would produce `{ "type": "string", "enum": [] }`, which Draft-07 accepts as a valid (unsatisfiable) schema (FR-315 final sentence).

#### 3.4.7 `PropertyRenderer.Render`

`Render(string name, TypeRef typeRef, ImmutableArray<AnnotationDecl> annotations, model, mvFqns, diags)` returns the property's value schema fragment. Composition order inside the returned `JsonObject`:

1. The base type fragment from `TypeMapper.MapTypeRef(typeRef, model, mvFqns)` — primitives produce the FR-330 table row; array modifier wraps in `{ "type": "array", "items": <inner> }`; named types produce a `$ref` per FR-318. The optional modifier is NOT encoded in the type fragment (FR-331); it controls the enclosing `required` array only.
2. `@json_schema(...)` annotation keys folded in by `AnnotationFolder.Fold(fragment, annotations, propertyName, diags)` (§3.6).

The property name itself is the dictionary key in the enclosing `properties` map; the renderer does not emit it. Annotations on positions other than `PropertyDecl` (relations, events, commands, etc.) are silently ignored in v1 (FR-343).

### 3.5 `TypeMapper`

`Gravity.Dsl.Emitter.JsonSchema/TypeMapper.cs` is a closed-form pure-function module — no I/O, no diagnostics surfacing, no mutable state. Three public methods:

```csharp
internal static class TypeMapper
{
    /// <summary>
    /// FR-330 primitive table. Pure function: input determines output byte-for-byte.
    /// Modifiers (FR-331 / FR-332) are applied by Wrap below; this method emits the
    /// raw type fragment only.
    /// </summary>
    public static JsonNode MapPrimitive(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.String   => new JsonObject { ["type"] = "string" },
        PrimitiveKind.Int      => new JsonObject { ["type"] = "integer",
                                                   ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue },
        PrimitiveKind.Long     => new JsonObject { ["type"] = "integer",
                                                   ["minimum"] = long.MinValue, ["maximum"] = long.MaxValue },
        PrimitiveKind.Decimal  => new JsonObject { ["type"] = "string", ["format"] = "decimal" },
        PrimitiveKind.Boolean  => new JsonObject { ["type"] = "boolean" },
        PrimitiveKind.Date     => new JsonObject { ["type"] = "string", ["format"] = "date" },
        PrimitiveKind.DateTime => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        PrimitiveKind.UUID     => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
        _ => throw new InvalidOperationException($"unknown PrimitiveKind '{kind}'"),
    };

    /// <summary>
    /// FR-333 named-type ref. Resolves through model.Declarations: if the named type
    /// is namespace-scope (the only legal placement in v1 grammar), produces a
    /// cross-file $ref. When the target's namespace differs from the referrer's,
    /// the path uses ../<other-namespace-path>/ form (FR-318).
    /// </summary>
    public static JsonNode MapNamedType(NamedTypeRef named, string referrerNamespace, ResolvedModel model,
                                        IReadOnlySet<string> mvFqns)
    {
        var (targetNs, targetName) = ResolveTarget(named, model);
        string fileName = mvFqns.Contains($"{targetNs}.{targetName}") && named.Version is int v
            ? $"{targetName}.v{v}.json"
            : $"{targetName}.json";
        string refPath = string.Equals(targetNs, referrerNamespace, StringComparison.Ordinal)
            ? fileName
            : ComposeRelativeRefPath(referrerNamespace, targetNs, fileName);
        return new JsonObject { ["$ref"] = refPath };
    }

    /// <summary>
    /// FR-314 step 3 relation encoding: cardinality-one → "<name>_id" UUID property;
    /// cardinality-many → "<name>_ids" UUID array with uniqueItems=true. The relation's
    /// Semantic clause folds into the property entry's description field.
    /// </summary>
    public static (string PropertyName, JsonNode Fragment) MapEntityRelation(RelationDecl rel)
    {
        bool many = rel.Cardinality is Cardinality.ZeroToMany or Cardinality.OneToMany;
        string propName = rel.Name + (many ? "_ids" : "_id");
        JsonNode core = many
            ? new JsonObject {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                ["uniqueItems"] = true }
            : new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        // FR-314 final sentence: the Semantic clause flows into description.
        string description = rel.Semantic is string s
            ? $"references {rel.Target} by id ({s})"
            : $"references {rel.Target} by id";
        ((JsonObject)core)["description"] = description;
        return (propName, core);
    }

    /// <summary>FR-331 / FR-332 modifier composition. Optional drops at the array-items boundary.</summary>
    public static JsonNode WrapTypeRef(JsonNode innerFragment, TypeRef typeRef)
    {
        // FR-332: T?[] and T[] produce identical items fragments; the inner '?' is dropped on the
        // items fragment with no diagnostic (JSON arrays don't have absent slots).
        if (typeRef.IsArray)
            return new JsonObject { ["type"] = "array", ["items"] = innerFragment };
        return innerFragment;
    }
}
```

`ResolveTarget` walks `model.Declarations` to find the FQN whose simple name matches `named.Name` (Phase 0–3 resolver guarantees a single match for legal source). `ComposeRelativeRefPath` produces `../<other-ns-path>/<file>` for cross-namespace references; for the v1 `samples/registry/` corpus this path is exercised but every named-type ref happens to be same-namespace, so the cross-namespace path is unit-tested on a synthetic fixture (AC-4.7).

### 3.6 `AnnotationFolder`

`Gravity.Dsl.Emitter.JsonSchema/AnnotationFolder.cs` reads the property's `@json_schema(...)` annotations and folds the claimed keyword subset onto the fragment in-place. The claimed set is closed (FR-340): `{format, pattern, description, examples, minLength, maxLength, minimum, maximum, multipleOf}`. Per-key value-type contracts (FR-341) are enforced here; mismatches produce `JS001`.

```csharp
internal static class AnnotationFolder
{
    private static readonly ImmutableHashSet<string> KnownFormats = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "email", "uri", "uuid", "date", "date-time", "time", "hostname", "ipv4", "ipv6", "regex", "decimal");

    public static void Fold(JsonObject fragment, ImmutableArray<AnnotationDecl> annotations,
                            string propertyName, string entityFqn,
                            ImmutableArray<Diagnostic>.Builder diags)
    {
        foreach (var ann in annotations.Where(a => a.Namespace == "json_schema"))
        {
            foreach (var (key, value) in ann.Arguments)  // ImmutableSortedDictionary — already ordinal
            {
                switch (key)
                {
                    case "format":
                        if (!TryString(value, out var fmt)) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "string", value); break; }
                        fragment["format"] = fmt;
                        if (!KnownFormats.Contains(fmt))
                            diags.Add(Diagnostic.Warning(JsonRuleIds.Js004,
                                $"property '{propertyName}' on '{entityFqn}': @json_schema(format: \"{fmt}\") is not in the emitter's known format set",
                                ann.Span));
                        break;

                    case "pattern":
                    case "description":
                        if (!TryString(value, out var s)) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "string", value); break; }
                        fragment[key] = s;
                        break;

                    case "examples":
                        if (!TryString(value, out var ex)) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "string", value); break; }
                        fragment["examples"] = new JsonArray(ex);   // FR-341: wrap single string into one-element array
                        break;

                    case "minLength":
                    case "maxLength":
                        if (!TryInt(value, out var len) || len < 0) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "non-negative integer", value); break; }
                        fragment[key] = len;
                        break;

                    case "minimum":
                    case "maximum":
                        if (!TryNumeric(value, out var num)) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "integer or decimal", value); break; }
                        fragment[key] = num;
                        break;

                    case "multipleOf":
                        if (!TryNumeric(value, out var mult)) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "integer or decimal", value); break; }
                        if (IsZero(mult)) { Js001Mismatch(diags, ann, propertyName, entityFqn, key, "non-zero number (Draft-07 forbids multipleOf: 0)", value); break; }
                        fragment[key] = mult;
                        break;

                    default:
                        diags.Add(Diagnostic.Error(JsonRuleIds.Js001,
                            $"property '{propertyName}' on '{entityFqn}': unknown json_schema key '{key}'; claimed keys are {{format, pattern, description, examples, minLength, maxLength, minimum, maximum, multipleOf}}",
                            ann.Span));
                        break;
                }
            }
        }
    }
}
```

**Why emitter-side and not validator-side.** Phase 0–3 `VAL006` ("unclaimed annotation namespace") only fires for namespaces that no registered emitter claims. `json_schema` IS claimed, so the validator pass succeeds; the per-key contract enforcement has to happen one layer down, inside the emitter that actually knows which keys are meaningful in its dialect. This is the documented split per FR-342 and §6 risk register (annotation analyzer / emitter validation split row). The diagnostic format (FR-365) names the rule id, the entity FQN, the property name, the offending key, the expected value type, and the offending value's literal text where available.

### 3.7 Determinism mechanism — `SortedKeyJsonWriter`

`Gravity.Dsl.Emitter.JsonSchema/SortedKeyJsonWriter.cs` is the central determinism hinge. It accepts a `JsonNode` (mutable, populated by the renderers in canonical declaration order) and writes it as UTF-8 with `\n` line endings, 2-space indent, no BOM. The writer enforces:

- **(a) Canonical key order on every schema object (FR-351).** A fixed canonical list `["$schema", "title", "x-gravity-version", "type", "properties", "required", "additionalProperties", "enum", "definitions"]`. Keys not in the canonical list (annotation-folded `description`, `format`, `pattern`, `minLength`, `maxLength`, `minimum`, `maximum`, `multipleOf`, `examples`, plus the relation-encoded `items`, `uniqueItems`) appear after the canonical list, sorted ordinally. The `$ref` key short-circuits the canonical order: a `JsonObject` carrying `$ref` is treated as a Draft-07 reference object and serialised with `$ref` first, then any sibling keys sorted ordinally — this matches the Draft-07 convention that a `$ref` object's siblings are conventionally ignored by validators.
- **(b) Sorted `required` arrays and `definitions` keys (FR-353, FR-355).** `required` arrays are written in ordinal sort order regardless of insertion order; `definitions` entries are written sorted by key (ordinal). Property maps (FR-352) and enum arrays (FR-354) preserve insertion order — which is DSL declaration order because the renderers walk `entity.Properties`, `entity.Events`, `entity.Commands`, etc. in source order.
- **(c) LF newlines + UTF-8 no BOM.** `System.Text.Json.Utf8JsonWriter` is configured with `JsonWriterOptions { Indented = true, IndentCharacter = ' ', IndentSize = 2, NewLine = "\n" }`. The resulting buffer is wrapped through `Encoding.UTF8.GetString` (which is BOM-less by default in .NET when constructed without `encoderShouldEmitUTF8Identifier: true`); a final `.Replace("\r\n", "\n")` guards against any host platform that might inject CRLF through the indentation logic. The string handed to `IEmitterOutput.WriteFile` is then written to disk by `BufferedEmitterOutput.CommitTo` using its existing UTF-8-no-BOM + LF pipeline (BufferedEmitterOutput.cs:105–112).
- **(d) Mutable `JsonObject` as intermediate.** Renderers produce `JsonObject` trees freely (in any order); `SortedKeyJsonWriter.Serialize(root)` is the single funnel through which output bytes are produced. No renderer calls `System.Text.Json.JsonSerializer.Serialize` directly — that would bypass the canonical order. A unit test asserts this invariant via reflection on the renderer types (no `JsonSerializer.Serialize` symbol referenced anywhere under `Render/`).

The writer's full algorithm in pseudocode:

```
Serialize(JsonNode root):
  using a Utf8JsonWriter over a MemoryStream with JsonWriterOptions{Indented=true, IndentCharacter=' ', IndentSize=2, NewLine="\n"}
    WriteNode(writer, root)
  return Encoding.UTF8.GetString(memory).Replace("\r\n", "\n")

WriteNode(writer, node):
  if node is JsonObject obj:
    writer.WriteStartObject()
    if obj.ContainsKey("$ref"):
      writer.WriteString("$ref", obj["$ref"])
      foreach key in obj.Keys.Where(k != "$ref").OrderOrdinal():
        WritePropertyAndValue(writer, key, obj[key])
    else:
      foreach key in CanonicalOrder.Where(obj.ContainsKey):
        if key in {"required"}:                      // FR-353 sorted ordinal
          WriteSortedStringArray(writer, key, obj[key])
        elif key == "definitions":                          // FR-355 sorted ordinal by key
          WriteSortedKeyObject(writer, key, obj[key])
        else:
          WritePropertyAndValue(writer, key, obj[key])
      foreach key in obj.Keys.Except(CanonicalOrder).OrderOrdinal():
        WritePropertyAndValue(writer, key, obj[key])
    writer.WriteEndObject()
  elif node is JsonArray arr:
    writer.WriteStartArray()
    foreach element in arr: WriteNode(writer, element)
    writer.WriteEndArray()
  else:
    writer.WriteValue(node)
```

This is the same "buffer-then-flush-in-canonical-order" pattern `BufferedEmitterOutput` uses for files-as-a-whole; the writer applies it one level down to keys-within-an-object.

### 3.8 NuGet packaging — `Gravity.Dsl.Emitter.JsonSchema.csproj`

The packaging-relevant csproj properties mirror `Gravity.Dsl.Emitter.CSharp.csproj` exactly (which itself follows the repo-wide `Directory.Build.props` patterns); no carve-outs needed:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>Gravity.Dsl.Emitter.JsonSchema</PackageId>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <Description>JSON Schema (Draft-07) reference emitter for the Gravity DSL.</Description>
    <Deterministic>true</Deterministic>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <ContinuousIntegrationBuild Condition=" '$(CI)' == 'true' ">true</ContinuousIntegrationBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Gravity.Dsl.Ast\Gravity.Dsl.Ast.csproj" />
    <ProjectReference Include="..\Gravity.Dsl.Emitter\Gravity.Dsl.Emitter.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- buildTransitive contribution to the Phase 9 <GravityDslEmitterAssembly> item; lets
         consumers pick the emitter up by adding a single <PackageReference> alongside
         Gravity.Dsl.MsBuild. Same shape as the outline sample's .props. -->
    <None Include="buildTransitive\Gravity.Dsl.Emitter.JsonSchema.props"
          Pack="true" PackagePath="buildTransitive\Gravity.Dsl.Emitter.JsonSchema.props" />
  </ItemGroup>

</Project>
```

The package's runtime layout is the standard NuGet `lib/net9.0/Gravity.Dsl.Emitter.JsonSchema.dll`. **No `tasks/` tree, no `tools/` tree** — the emitter is loaded by `EmitterRegistry.Discover` at host runtime (Phase 0–3 contract), not as a build task itself. The banned-APIs analyzer is enabled (the emitter inherits the analyzer scope from `Directory.Build.props`; **no carve-out** is sought — JSON Schema generation has no legitimate need for `DateTime.UtcNow`, `Environment.MachineName`, `Path.GetTempFileName`, or any other banned symbol; FR-362).

The only dependency at runtime is `System.Text.Json`, which is part of the .NET 9 BCL and does not appear as a `PackageReference`. The emitter does NOT depend on `Newtonsoft.Json`, `JsonSchema.Net`, `Manatee.Json`, or any other third-party JSON library (FR-363). The validator dependency (`JsonSchema.Net`, used in `SchemaValidityTests.cs` for AC-4.4 metaschema validation) lives in `Gravity.Dsl.Tests.csproj` with `PrivateAssets="all"` so it does NOT propagate to the emitter package's transitive graph.

### 3.9 Plug-in registration

The emitter is discovered by `EmitterRegistry.Discover` (Phase 0–3) via assembly scan — same mechanism every other emitter uses. There is no Phase 4-specific MSBuild wiring beyond shipping the `buildTransitive/Gravity.Dsl.Emitter.JsonSchema.props` file that contributes the emitter assembly to the `<GravityDslEmitterAssembly>` MSBuild item established by Phase 9 FR-224. The contents of the props file mirror the outline sample emitter's verbatim:

```xml
<Project>
  <ItemGroup>
    <GravityDslEmitterAssembly Include="$(MSBuildThisFileDirectory)..\lib\net9.0\Gravity.Dsl.Emitter.JsonSchema.dll" />
  </ItemGroup>
</Project>
```

For consumers who reference `Gravity.Dsl.Emitter.JsonSchema` directly (via NuGet `<PackageReference>` alongside `Gravity.Dsl.MsBuild`), the wiring is automatic. For consumers who use the standalone CLI, the emitter DLL must be discoverable by `EmitterRegistry.Discover` — which is the case when the DLL is in the host's plugin path (typically `tasks/net9.0/` under the `Gravity.Dsl.MsBuild` package layout, or on the CLI's discovery search path). `samples/registry/.gravity.yaml` is extended to include a documented example `json-schema:` block; the existing Phase 0–3 sample configs are not otherwise touched.

## 4. Determinism strategy

Phase 4 introduces JSON serialisation, which is the new determinism surface to defend. Five concrete commitments preserve the project-wide byte-identical-across-runs guarantee. They map 1:1 to FR-350..FR-355 / AC-4.9 and complement the existing Phase 0–3 / Phase 8 / Phase 9 determinism bar without replacing any of it.

- **(a) Canonical key order on every schema object (FR-351).** `SortedKeyJsonWriter` (§3.7) is the single funnel through which any `JsonObject` becomes bytes. The fixed canonical list is encoded inline in the writer and enumerated in deterministic order; non-canonical keys overflow to a sorted ordinal tail. No renderer bypasses the writer — a unit test asserts this via reflection (`typeof(JsonSerializer).GetMethod("Serialize")` is never referenced from any type under `Gravity.Dsl.Emitter.JsonSchema/Render/`).
- **(b) Sorted `required` arrays and `definitions` keys (FR-353, FR-355).** The writer sorts these two specific structures at write time, not at AST-walk time. Adding a new event to an entity produces a one-line diff in the bundle file: a new `definitions` entry slotted into ordinal position. The asymmetry with `properties` (FR-352, declaration order) and `enum` (FR-354, declaration order) is intentional — sets versus sequences.
- **(c) LF line endings, UTF-8 no BOM (FR-350).** Post-serialise `.Replace("\r\n", "\n")` guards against any host-platform CRLF injection from the indentation logic. The encoding pipeline through `BufferedEmitterOutput.CommitTo` (BufferedEmitterOutput.cs:105–112) is already UTF-8-no-BOM + LF for every emitter; the JSON Schema emitter inherits that pipeline unchanged.
- **(d) Cross-platform CI byte-compare (AC-4.9).** CI matrix runs the golden suite on Linux and macOS (the same two-row matrix Phase 0–3 established; Windows is not part of the matrix until further notice per spec AC-4.9). A byte-identical assertion across the two legs catches any platform-conditional drift before merge.
- **(e) Banned-API analyzer prevents non-deterministic API use (FR-362).** The analyzer is enabled on `Gravity.Dsl.Emitter.JsonSchema` with **no carve-out**. `DateTime.UtcNow`, `Environment.MachineName`, `Path.GetTempFileName`, `Guid.NewGuid`, `Stopwatch`, and the other banned non-deterministic surfaces remain inaccessible. The packaging path (FR-361) inherits `Deterministic=true` + `EmbedUntrackedSources=true` + `ContinuousIntegrationBuild=true` from `Directory.Build.props`; `dotnet pack` twice on a clean tree produces byte-identical `.nupkg` files, asserted by a test alongside the artifact-determinism test.

## 5. Test strategy

Six tiers mirroring the predecessor plan's three but expanded for the new validator-side surface. All tiers run on every PR.

### 5.1 Type mapping tests (AC-4.5)

`Gravity.Dsl.Tests/Emitter/JsonSchema/TypeMappingTests.cs` is xUnit-parameterised over every primitive in `PrimitiveKind` × every modifier combination in `{ T, T?, T[], T[]? }`. 8 primitives × 4 combos = 32 base cases, plus a small handful of named-type-ref combinations exercising `MapNamedType` resolution. Each test loads a single-property fixture `.gravity` source, runs the emitter, and byte-compares the emitted entity bundle's `properties` map against a golden under `tests/golden/json-schema/primitive-matrix/`.

Two reinforcement assertions beyond plain golden compare:

1. **Long range pinning.** The `Long` fixture's golden explicitly carries `"minimum": -9223372036854775808, "maximum": 9223372036854775807` so a regression that overflows to int32 bounds fails the test (cross-pins FR-330 row 3).
2. **`String?[]` and `String[]` produce identical `items` fragments (FR-332).** A dedicated test asserts this asymmetry: the surrounding property name appears in `required` for the former but not the latter; the inner `items` fragment is byte-identical.

### 5.2 Schema validity tests (AC-4.4)

`Gravity.Dsl.Tests/Emitter/JsonSchema/SchemaValidityTests.cs` uses `JsonSchema.Net` (test-project-only dependency, `PrivateAssets="all"`, FR-363) to perform two distinct validations on every emitted file:

1. **Metaschema validation.** Each of the 20 emitted files from `samples/registry/` is loaded through `JsonSchema.Net.JsonSchema.FromText` and validated against the Draft-07 metaschema `http://json-schema.org/draft-07/schema#`. All 20 must pass. This is the load-bearing AC-4.4 assertion.
2. **Instance validation.** For each of the 3 entity bundles (`Employee.json`, `TimeEntry.json`, `Project.json`), a hand-authored minimal instance fixture under `tests/fixtures/json-schema/instances/<Entity>.instance.json` is validated against the bundle. The instances are deliberately minimal (identity field, required properties, `state` set to one of the declared lifecycle states); they live in the test tree, not the emitter source (a generator would conflict with the "hand-authored fixtures" requirement of AC-4.4).

The `JsonSchema.Net` package is pinned in `Directory.Packages.props` to its latest stable version (~7.x at lock date); the version is recorded with a comment explaining "test-only, kept out of the emitter's transitive graph by PrivateAssets=all on the test csproj's PackageReference".

### 5.3 Golden-file tests (AC-4.11)

`Gravity.Dsl.Tests/Emitter/JsonSchema/GoldenFileTests.cs` mirrors `Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs` line-for-line. It runs the emitter against `samples/registry/`, asserts every file in `tests/golden/json-schema/registry/` is produced and byte-matches, AND asserts the emitter produces no extra files outside the golden set. The shape of both helpers is identical: `RunJsonSchemaEmitter()` returns an `ImmutableSortedDictionary<string, string>` snapshot, two `[Fact]` methods exercise it.

Supports the standard `GRAVITY_UPDATE_GOLDENS=1` environment variable already wired in Phase 0–3 / Phase 9 for selective regeneration when a deliberate output change lands. The 20-file golden tree (3 entity bundles + 15 value-type files (1 ContactInfo + 14 result types) + 2 enum files) is pinned at initial lock; subsequent changes require a deliberate update to the golden files with reviewer approval (constitution "Generated artifacts look hand-written" quality bar).

### 5.4 Determinism tests (AC-4.9)

`Gravity.Dsl.Tests/Emitter/JsonSchema/DeterminismTests.cs` mirrors `Gravity.Dsl.Tests/Emitter/CSharp/DeterminismTests.cs` exactly. It runs the JSON Schema emitter twice in-process against `samples/registry/` and asserts the resulting `BufferedEmitterOutput` snapshots are byte-identical (same file set, every file byte-equal). Two extensions beyond the C# template:

1. **Pack-determinism check.** A separate `[Fact]` runs `dotnet pack Gravity.Dsl.Emitter.JsonSchema.csproj -c Release` twice into two temp directories and `sha256sum`s the resulting `.nupkg`s. The two hashes must match (FR-361, AC-4.9 third sentence).
2. **Same-byte cross-platform guarantee.** Covered by CI matrix running this test on both Linux and macOS lanes; no additional test code needed.

### 5.5 Registration tests (AC-4.2)

`Gravity.Dsl.Tests/Emitter/JsonSchema/RegistrationTests.cs` mirrors `Gravity.Dsl.Tests/Emitter/Outline/RegistrationTests.cs` shape:

1. **TargetName / AnnotationNamespace / SupportedAstVersions.** Constructs a `JsonSchemaEmitter` and asserts `TargetName == "json-schema"`, `AnnotationNamespace == "json_schema"`, `SupportedAstVersions.Satisfies("1.0.0") && SupportedAstVersions.Satisfies("1.1.0")` (FR-301, LD-17).
2. **HOST002 cross-claim.** Registers `JsonSchemaEmitter` alongside a stub emitter whose `AnnotationNamespace = "json_schema"` and asserts `EmitterRegistry.FromInstances(...)` surfaces `HOST002` naming both claimants in sorted-ordinal order, aborting before any emitter executes (mirrors Phase 0–3 AC-5, Phase 9 AC-9.10).
3. **Config schema shape.** Asserts `ConfigurationSchema.Keys` has exactly two entries — `output` (Required: true, Default: null) and `bundle_strategy` (Required: false, Default: "per-entity") — in that order (FR-302).

### 5.6 Annotation folding tests (AC-4.8, AC-4.12)

`Gravity.Dsl.Tests/Emitter/JsonSchema/AnnotationFoldingTests.cs` exercises every `@json_schema(...)` key + every JS001 / JS004 sub-cause:

| Fixture | Expected outcome |
|---|---|
| `email: String @json_schema(format: "email", pattern: "^.+@.+$", description: "Primary email", minLength: 3, maxLength: 254)` | Fragment carries `"type": "string"` then sorted: `"description"`, `"format"`, `"maxLength"`, `"minLength"`, `"pattern"` (FR-351 sorted overflow). Byte-checked against a golden. |
| `amount: Decimal @json_schema(format: "decimal", multipleOf: 0.01)` | Fragment carries `"type": "string"`, `"format": "decimal"`, `"multipleOf": 0.01`. |
| `@json_schema(format: 42)` on a string property | One `JS001` ("format expects a string value, got integer literal 42"). |
| `@json_schema(minLength: "ten")` | One `JS001` ("minLength expects a non-negative integer value, got string literal \"ten\""). |
| `@json_schema(multipleOf: 0)` | One `JS001` ("multipleOf expects a non-zero number; Draft-07 forbids multipleOf: 0"). |
| `@json_schema(unknown_key: "x")` | One `JS001` ("unknown json_schema key 'unknown_key'; ..."). |
| `@json_schema(format: "ipv6")` | Emits cleanly; fragment carries `"format": "ipv6"`. |
| `@json_schema(format: "duration")` | One `JS004` (warning, non-blocking); fragment still carries `"format": "duration"` verbatim. |
| `@json_schema(format: "email")` on a `RelationDecl` | Silently ignored per FR-343; no diagnostic; relation fragment carries no `"format"` key. |

Each fixture is byte-checked plus its diagnostic count and rule-id set asserted via FluentAssertions. The fixtures live under `tests/fixtures/json-schema/annotations/` (one `.gravity` file per row).

### 5.7 Cross-emitter integration test (AC-4.10)

`Gravity.Dsl.Tests/Emitter/HostIntegrationTests.cs` already exists from Phase 9 (asserts the C# emitter and outline emitter co-exist without HOST002). Phase 4 extends it with a new `[Fact]` that enrols all three emitters — `CSharpEmitter`, `OutlineEmitter`, `JsonSchemaEmitter` — into the same `EmitterRegistry.FromInstances` call and asserts:

1. Zero `HOST002` diagnostics (the three namespaces `csharp`, `outline`, `json_schema` are disjoint).
2. A `samples/registry/`-style fixture using `@json_schema(format: "email")` on one property and `@csharp(record_struct: true)` on another passes validation through `Validator.Validate(model, registry.ClaimedAnnotationNamespaces())`.
3. The same fixture with an extra `@graphql(...)` annotation on a property produces a `VAL006` ("unclaimed annotation namespace") because no GraphQL emitter is enrolled — pinning that the split between `VAL006` (validator, namespace-level) and `JS001` (emitter, key-level) is correct (FR-342, §6 risk register annotation analyzer / emitter validation split row).

The test does NOT run any emitter; it stops at validator pass. The complementary "all three emit cleanly against samples/registry" check is covered by `GoldenFileTests` for each emitter separately.

## 6. Risk register (Phase 4 surface)

| Risk | Surface in Phase 4 | Mitigation |
|---|---|---|
| `Decimal` as string format unrecognised by validators. | `"format": "decimal"` is the emitter's own vendor format; not a Draft-07-registered format. Some validators may warn or fail on unknown formats by default. | Document explicitly in `README.md` and in a comment block at the top of `TypeMapper.cs`. The emitter declares "decimal" in its `KnownFormats` set so its own pass through `AnnotationFolder` does not raise `JS004` against itself. Consumers can configure their validators to ignore unknown formats (AJV: `strict: false`; JsonSchema.Net: `ValidationOptions.AddCustomKeyword(...)`). Recommend Decimal-as-string for financial / regulatory values where the lossless requirement is non-negotiable. |
| `Long` values exceeding JS safe-integer range. | JSON's `number` is IEEE-754 double; `Long` values outside ±2^53 lose precision when consumed by JavaScript-stack validators. | Document in `README.md` and in `TypeMapper.cs` row comment. Recommend `Decimal`-as-string for values likely to exceed 2^53. AJV and JsonSchema.Net handle the full int64 range correctly (they decode integers as their native 64-bit type), so the schema is honest about the range even if some downstream parsers truncate. |
| Property order vs key sort inconsistency. | DSL declaration order for `properties` (FR-352) but ordinal sort for `required` (FR-353) and `definitions` (FR-355) — risk of a renderer accidentally sorting `properties` or accidentally preserving declaration order in `required`. | Codified in §3.7. One golden fixture exercises both orderings simultaneously (`tests/golden/json-schema/primitive-matrix/MixedOrdering.json` — three properties declared in non-alphabetical order, two of them required; the property map is in declaration order, the required array is sorted ordinally). Reviewer reads the golden and confirms by eye. |
| `format` keyword value drift across drafts. | JSON Schema's set of "registered" formats varies by draft and by validator; users may pass non-registered format strings (`"duration"` from Draft 2019-09+, custom domain formats). | `JS004` warning for unknown format strings (non-blocking) — the emitter passes the format through verbatim so consumer validators that accept it work; consumer validators that reject unknown formats can configure their leniency. A future config knob `strict_format` could promote `JS004` to `JS001` (Error) in a later version; deferred to Phase 5+. |
| Cross-file `$ref` vs within-bundle `$ref` confusion. | Hard-coding `<Name>.json` for cross-file refs may break when value types share a name across namespaces, or when an entity-local value type (none in v1 grammar but reserved) is mis-classified as namespace-scope. | `TypeMapper.MapNamedType` walks the resolved model to determine whether a `NamedTypeRef` points within-bundle (entity-local `definitions` entry) or cross-file (namespace-scope file). Cross-namespace refs use `../<other-ns-path>/<Name>.json`. AC-4.7 pins both legs of the resolution with a synthetic fixture. |
| Validator dep size in test project. | `JsonSchema.Net` is ~500KB; pulling it into `Gravity.Dsl.Tests` increases test build time and restore graph. | Mark `JsonSchema.Net` `<PackageReference>` with `PrivateAssets="all"` so it does not propagate to any dependent of `Gravity.Dsl.Tests` (there are none, but defence-in-depth). Confine its use to `SchemaValidityTests.cs` only; other test files do not import it. Document in `Directory.Packages.props` comment alongside the version pin. |
| Annotation analyzer / emitter validation split. | Phase 0–3 `VAL006` handles unclaimed-namespace cases at the validator layer; Phase 4 `JS001` handles per-key contract violations inside a claimed namespace. Risk: a future contributor confuses the two layers, adds `JS001` cases to the validator (or `VAL006` cases to the emitter). | Documented split: validator continues to handle namespace-level (`VAL006`); emitter handles key-level (`JS001` / `JS002` / `JS004`). The boundary is encoded in §3.6 and reinforced by `tests/integration/cross-emitter-validation/` exercising both diagnostic flavors against the same fixture (AC-4.10 row 3). |
| Multi-version filename rule edge case. | FR-310's `.v<N>.json` infix applies symmetrically (every version when more than one version is in scope, including version 1). Risk: an off-by-one bug emits `Employee.json + Employee.v2.json` instead of `Employee.v1.json + Employee.v2.json`. | `JsonSchemaEmitter.ComputeMultiVersionFqns` is a single helper, unit-tested directly on synthetic `ResolvedModel` instances. A golden under `tests/golden/json-schema/multi-version/` exercises a two-version fixture; the file set is asserted to be exactly `{Employee.v1.json, Employee.v2.json}` with no unqualified `Employee.json`. |

## 7. Out-of-scope acknowledgements

Documented here so they do not creep in. Each item maps to a spec non-goal (NG-1..NG-11) or is carried over from earlier phases. Phase 4 ships ONE reference emitter; future phases extend the emitter set, the dialect surface, and the authoring story.

- **JSON Schema Draft 2020-12 / 2019-09 / Draft-06 dialect** (NG-1). Phase 4 emits Draft-07 exclusively. A future `--draft 2020-12` flag is Phase 10+ and is NOT delivered here; the emitter has no multi-dialect dispatch logic.
- **OpenAPI emission** (NG-2). Phase 6 (`Gravity.Dsl.Emitter.OpenApi`) is a separate emitter and a separate spec. The JSON Schema emitter does not produce an OpenAPI document, does not embed schemas in an OpenAPI envelope, and does not co-emit a `components/schemas` section.
- **AsyncAPI emission** (NG-3). Phase 7 (`Gravity.Dsl.Emitter.AsyncApi`) is independent. Per-event payload schemas from this emitter can be referenced by a future AsyncAPI emitter via `$ref`, but no inter-emitter integration is built in Phase 4.
- **Absolute `$id` URIs** (NG-4). Phase 4 uses relative `$ref` only. No `$id` carries a registry URL, a CDN base, or any non-relative identifier. Schemas are filesystem-relocatable; consumers who need absolute URIs run a post-processor or wait for the Registry layer (Principle VII keeps the DSL Registry-agnostic).
- **Per-entity-version `$id` segments** (NG-5). No `$id` with a version segment in v1. Multi-version coexistence is reflected in the file path layout (FR-310 `.v<N>.json` infix) and in the `x-gravity-version` vendor extension; consumers choose which file to validate against.
- **TypeScript / Python codegen FROM JSON Schema** (NG-6). Tools like `quicktype`, `json-schema-to-typescript`, and `datamodel-codegen` are downstream user concerns. The emitter outputs JSON Schema; what consumers do with it is out of scope.
- **Runtime validation library** (NG-7). Selecting a validator (AJV, JsonSchema.Net, Python `jsonschema`, gateway-native engine) is the consumer's decision. The emitter does not ship a runtime, does not recommend one in the generated artifact, and does not encode validator-specific extensions.
- **Schema migration / diff tooling** (NG-8). No `gravc schema-diff v1 v2` command, no migration generator, no breaking-change detector at the schema layer. The Phase 8 breaking-change pass (`VAL020..VAL030`) runs at the DSL layer and is the single authoritative diff surface; emitted schemas inherit its guarantees.
- **Hand-tuneable output formatting** (NG-9). No `indent: 4`, no `key_order: explicit`, no `pretty: false` knobs in the configuration schema. Determinism is the only formatting contract; knobs that compromise byte-stability are refused.
- **JSON Pointer escaping of name segments** (NG-10). DSL identifiers are ASCII letter / digit / underscore beginning with a letter (FR-004); they never require `~0` / `~1` escaping. If a future grammar extension permits richer identifiers, escaping becomes a follow-up; Phase 4 does not anticipate it.
- **Phase 8 multi-version bundle filename rule edge case** (FR-310). The v1 shipping `samples/registry/` has no multi-version FQN, so the `.v<N>.json` code path is exercised by a unit-test fixture under `tests/golden/json-schema/multi-version/` only. The renderer is wired and tested; production samples just don't trigger it at lock date.
- **All Phase 0–8 + Phase 9 NG carry-overs** (NG-11). Registry features (scopes, permissions, rules, releases, library imports — Principle VII); storage backends; broker topology; deployment targets; AI authoring tooling; LSP / formatter / linter; runtime DSL evaluation; Roslyn source generator; watch mode; per-build telemetry; CLI ergonomics polish; emitter authoring guide (Phase 9b). Not addressed in Phase 4.

## 8. Revision history

- 2026-05-18 — Initial lock. Phase 4 implementation plan authored against `spec.md` (Phase 4 narrowed slice). Three sub-phases sequenced (P4a project scaffold + IEmitter contract, P4b schema generation + type mapping + annotation folding, P4c tests + goldens + cross-emitter integration); project layout, module-level architecture (8 subsections covering emitter shape, config, rule ids, seven renderers, type mapper, annotation folder, determinism mechanism, packaging, plug-in registration), determinism strategy (5 commitments), test strategy (7 tiers), risk register (8 rows), and out-of-scope acknowledgements (12 items) documented.
