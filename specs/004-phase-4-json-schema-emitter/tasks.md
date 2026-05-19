# Gravity DSL — Task Plan (Phase 4: JSON Schema reference emitter)

**Status:** Locked for implementation
**Date:** 2026-05-18
**Driven by:** `specs/004-phase-4-json-schema-emitter/plan.md`
**Predecessor:** `specs/003-phase-9-build-integration/tasks.md` (T199..T254). Phase 4 task ids begin at `T300` to keep the global task-id namespace flat and grep-friendly, leaving a deliberate gap between the Phase 9 ceiling (T254) and the Phase 4 floor (T300) so future Phase-9 follow-ups can land in the T255..T299 band without collision.

Conventions:
- Tasks numbered `T3##` in execution order. Sub-phase boundaries (P4a → P4b → P4c) are hard gates; a sub-phase's tasks complete before the next sub-phase begins.
- `[P]` marks tasks runnable in parallel with peers in the **same** sub-phase.
- Every task lists: **Acceptance** (verifiable; cites FR/AC from `spec.md`), **Files** (repo-relative paths touched), **Depends on** (prior T-numbers or `—`).
- Phase 0–3 / Phase 8 / Phase 9 tasks (T001..T254) remain locked. No Phase 4 task removes or weakens a Phase 0–3 / Phase 8 / Phase 9 acceptance condition. Phase 4 is strictly additive at the new-project layer (spec §6 "Cross-references", final paragraph).

---

## Sub-phase P4a — Project scaffold + IEmitter contract (T300–T314)

Goal: stand up the new `Gravity.Dsl.Emitter.JsonSchema/` project as a NuGet-packable sibling to `Gravity.Dsl.Emitter.CSharp/`, declare the `IEmitter` shell with the correct target name / annotation namespace / supported AST range, and wire it into the solution + emitter-host discovery surface. No schema generation happens here — P4a closes the contract and packaging perimeter so P4b can land renderers behind a fixed surface. Closes FR-300, FR-301, FR-302, FR-303, FR-304, FR-305, FR-360, FR-361, FR-362, FR-363, FR-364; AC-4.1, AC-4.2, AC-4.10. LD-14, LD-17.

### T300. Scaffold `Gravity.Dsl.Emitter.JsonSchema/` csproj
- **Acceptance.** New project `Gravity.Dsl.Emitter.JsonSchema/Gravity.Dsl.Emitter.JsonSchema.csproj` targets `net9.0`. Property group sets `<IsPackable>true</IsPackable>`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`. ProjectReferences point to `Gravity.Dsl.Ast/Gravity.Dsl.Ast.csproj` and `Gravity.Dsl.Emitter/Gravity.Dsl.Emitter.csproj` only — no reference to `Gravity.Dsl.Compiler`, `Gravity.Dsl.Emitter.CSharp`, or `Gravity.Dsl.Cli` (FR-300, FR-363; the emitter's dependency closure is AST + Emitter host + BCL `System.Text.Json` only). Inherits repo-wide `Directory.Build.props` settings (deterministic build, banned-APIs analyzer scope, treat-warnings-as-errors) without carve-out. Builds cleanly with `dotnet build`.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Gravity.Dsl.Emitter.JsonSchema.csproj`.
- **Depends on.** —

### T301 [P]. Wire `Gravity.Dsl.Emitter.JsonSchema` into the solution
- **Acceptance.** `Gravity.Dsl.sln` gains the new project entry via `dotnet sln add`. The solution still builds cleanly. The JSON Schema emitter project is NOT referenced by `Gravity.Dsl.Tests` directly via `ProjectReference` at this stage; tests under P4c will reference it.
- **Files.** `Gravity.Dsl.sln`.
- **Depends on.** T300.

### T302 [P]. NuGet packaging metadata + determinism flags
- **Acceptance.** `Gravity.Dsl.Emitter.JsonSchema.csproj` sets `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, `<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>`. Sets `<PackageId>Gravity.Dsl.Emitter.JsonSchema</PackageId>`, `<Description>JSON Schema (Draft-07) reference emitter for Gravity DSL — emits per-entity bundles, per-value-type, and per-enum schemas.</Description>`, `<RepositoryUrl>`, `<PackageTags>gravity dsl json-schema emitter</PackageTags>`. Apache-2.0 license matches the C# emitter package metadata exactly (FR-361). Per FR-361 the resulting `.nupkg` must be byte-identical across back-to-back packs on a clean tree; this is asserted by T355 / DeterminismTests.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Gravity.Dsl.Emitter.JsonSchema.csproj` (extend).
- **Depends on.** T300.

### T303. `JsonSchemaEmitter.cs` — IEmitter skeleton
- **Acceptance.** `Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitter.cs` declares `public sealed class JsonSchemaEmitter : IEmitter` with a public parameterless constructor (FR-305 — `EmitterRegistry.AppendEmittersFromAssembly` discovery requires both). Exposes `TargetName => "json-schema"` (LD-17 kebab-case — matches the `docs/specs.md` §5.3 example config block), `AnnotationNamespace => "json_schema"` (LD-17 underscore — annotation namespaces are identifiers per FR-050; hyphens are forbidden by FR-004), `SupportedAstVersions = SemanticVersionRange.Parse(">=1.0.0 <2.0.0")` (FR-301 — admits both the Phase 0–3 `1.0.0` AST and the Phase 8 `1.1.0` AST so the emitter compiles against either without a rebuild). `Emit(model, config, sink)` body is a stub that returns `EmitResult.Empty` and does not yet walk declarations; P4b lands the walk. No `partial`, no `virtual`. Per plan.md §3.1.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitter.cs`.
- **Depends on.** T300.

### T304. `JsonSchemaEmitterConfig.cs` — config schema + typed projection
- **Acceptance.** `JsonSchemaEmitter.ConfigurationSchema` exposes exactly two `ConfigKey` entries: `output` (`ConfigValueKind.String`, `Required: true`, no default) and `bundle_strategy` (`ConfigValueKind.String`, `Required: false`, `Default: "per-entity"`). `JsonSchemaEmitterConfig.cs` declares `internal sealed class JsonSchemaEmitterConfig` with `Output` and `BundleStrategy` properties and a `static From(EmitterConfig config, ImmutableArray<Diagnostic>.Builder diags)` factory that projects the validated config. Setting `bundle_strategy` to anything other than `"per-entity"` (including `"per-namespace"`, `"single-file"`, empty string) appends a `JS002` diagnostic and returns `null` (FR-302, FR-364). The synthetic span used for the diagnostic is the same target-name-only span pattern that `HOST001` / `HOST002` use; no new span shape is invented. Per plan.md §3.2.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitter.cs` (extend with `ConfigurationSchema`), `Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitterConfig.cs`.
- **Depends on.** T303.

### T305 [P]. `JsonRuleIds.cs` — JS001..JS010 constants
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/JsonRuleIds.cs` declares `internal static class JsonRuleIds` with constants `Js001`..`Js010`. Comments document each ruleId-to-FR mapping: `JS001` (unknown `@json_schema` key or annotation value-type mismatch or `multipleOf: 0`; severity Error; FR-341, FR-342, FR-364), `JS002` (bundle_strategy not `"per-entity"`; severity Error; FR-302, FR-364), `JS003` (user property name collides with reserved `state` on entity-state schema; severity Error; FR-315, FR-364), `JS004` (`@json_schema(format: "<value>")` carries an unknown-format string; severity Warning; FR-341, FR-364), `JS005..JS010` reserved for forward use and not consumed by this slice. `JS*` constants ship with the emitter assembly only and are NOT added to `Gravity.Dsl.Compiler/RuleIds.cs` (same separation pattern Phase 9 established for `MSB*`). Per plan.md §3.3.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/JsonRuleIds.cs`.
- **Depends on.** T300.

### T306. `SortedKeyJsonWriter.cs` — canonical-key-order JSON serializer
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/SortedKeyJsonWriter.cs` declares an internal helper that takes a `JsonNode` tree and emits UTF-8 bytes via `System.Text.Json.Utf8JsonWriter` with `JsonWriterOptions { Indented = true, NewLine = "\n" }` (or the .NET 9 pinned equivalent — the helper normalises). The writer enforces FR-351 canonical top-level key order (`$schema`, `title`, `x-gravity-version`, `type`, `properties`, `required`, `additionalProperties`, `enum`, `definitions`) followed by sorted-ordinal overflow keys. `properties` map entries are emitted in their declaration order (FR-352 — declaration order preserved; the helper does NOT alphabetise property keys). `required` arrays are sorted ordinally (FR-353). `enum` arrays are emitted in declaration order (FR-354 — semantic ordering matters). `definitions` keys are sorted ordinally (FR-355). UTF-8 no BOM. LF line endings only — never `Environment.NewLine`. Helper produces a deterministic byte sequence across runs and platforms (FR-350).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/SortedKeyJsonWriter.cs`.
- **Depends on.** T300.

### T307 [P]. `TypeMapper.cs` skeleton
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/TypeMapper.cs` declares `internal static class TypeMapper` with three method signatures only (bodies land in T320..T322): `JsonObject MapPrimitive(PrimitiveKind kind)`, `(string PropertyName, JsonObject Fragment) MapEntityRelation(RelationDecl relation)`, `JsonNode MapNamedType(NamedTypeRef typeRef, ResolvedModel model, IReadOnlySet<string> multiVersionFqns)`. Each signature compiles against the AST and emitter host surfaces. No logic in P4a; this task locks the contract that the P4b implementations are written against.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/TypeMapper.cs`.
- **Depends on.** T300.

### T308 [P]. `AnnotationFolder.cs` skeleton
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/AnnotationFolder.cs` declares `internal static class AnnotationFolder` with the method signature `void FoldOntoProperty(JsonObject fragment, ImmutableArray<AnnotationDecl> annotations, string propertyName, string entityFqn, ImmutableArray<Diagnostic>.Builder diags)`. Signature compiles; body is the stub that returns without folding (P4b's T323 lands the real folder). Locks the contract for downstream renderer use.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/AnnotationFolder.cs`.
- **Depends on.** T300.

### T309 [P]. Registration test `RegistrationTests.cs`
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/RegistrationTests.cs` instantiates `EmitterRegistry.FromInstances(new IEmitter[] { new JsonSchemaEmitter() })` and asserts the three contract values from FR-301: `TargetName == "json-schema"`, `AnnotationNamespace == "json_schema"`, `SupportedAstVersions.Satisfies(new SemanticVersion(1, 0, 0))` AND `SupportedAstVersions.Satisfies(new SemanticVersion(1, 1, 0))`. A second test method registers `JsonSchemaEmitter` alongside a stub emitter whose `AnnotationNamespace` also returns `"json_schema"` and asserts that `EmitterRegistry.FromInstances` surfaces `HOST002` naming both claimants in sorted-ordinal order, and the registry aborts before any emitter executes (mirrors Phase 0–3 AC-5; closes AC-4.2's ownership half). Adds a `<ProjectReference>` from `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` to `Gravity.Dsl.Emitter.JsonSchema/Gravity.Dsl.Emitter.JsonSchema.csproj`.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/RegistrationTests.cs`, `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj`.
- **Depends on.** T303.

### T310 [P]. Add `JsonSchema.Net` to `Directory.Packages.props` (test-only)
- **Acceptance.** `Directory.Packages.props` gains `<PackageVersion Include="JsonSchema.Net" Version="<latest 6.x or 7.x>" />`. The package is **not** consumed by `Gravity.Dsl.Emitter.JsonSchema/Gravity.Dsl.Emitter.JsonSchema.csproj` (FR-363 — emitter depends on BCL `System.Text.Json` only); it is consumed only by `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` for the metaschema-validation tests in T352 / T357. The reference is added to the test csproj with `PrivateAssets="all"` so the test dependency does not leak transitively into any consumer of the test assembly.
- **Files.** `Directory.Packages.props`, `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj`.
- **Depends on.** —

### T311 [P]. README.md in the emitter project
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/README.md` — one-paragraph description: "This package is the Gravity DSL JSON Schema (Draft-07) reference emitter. It is consumed alongside `Gravity.Dsl.MsBuild` via `<PackageReference>` in the same two-package layout the outline sample emitter documents. The emitter produces one JSON Schema bundle file per entity (entity-state root + per-event payload + per-command request/response in `definitions`), plus one stand-alone file per namespace-scope value type and per enum. Output is byte-deterministic Draft-07; configuration takes `output` (required) and `bundle_strategy: per-entity` (the only legal value in v1)."
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/README.md`.
- **Depends on.** —

### T312 [P]. Banned-APIs analyzer applies without carve-out
- **Acceptance.** `Gravity.Dsl.Emitter.JsonSchema` is **not** added to `Directory.Build.props`'s `BannedSymbolsFile` carve-out list (the list currently exempts `Gravity.Dsl.Cli`, `Gravity.Dsl.Tests`, `Gravity.Dsl.MsBuild`; this project does not join it). FR-362 mandates that `DateTime.UtcNow`, `Environment.MachineName`, `Path.GetTempFileName`, `Guid.NewGuid`, and the other banned non-deterministic surfaces remain inaccessible to the emitter codebase. Verified by a build that deliberately introduces `DateTime.UtcNow` in the emitter source failing with `RS0030` or the project's pinned banned-symbol diagnostic id, then reverting. No source code change is required for this task — it is a contract row confirming the project picks up the default analyzer scope automatically.
- **Files.** (no new files; this is a contract row that T352 and T354 enforce through end-to-end test runs)
- **Depends on.** T300.

### T313. Pack metadata test (PackContentTests-style)
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/PackContentTests.cs` runs `dotnet pack Gravity.Dsl.Emitter.JsonSchema -c Release --output <temp>`, opens the resulting `.nupkg` via `System.IO.Compression.ZipArchive`, and asserts the presence of exactly `lib/net9.0/Gravity.Dsl.Emitter.JsonSchema.dll` plus the standard NuGet `.nuspec` and `[Content_Types].xml` payload. Asserts the **absence** of: any `tasks/` entries (the emitter is loaded by `EmitterRegistry.Discover` at host runtime, not as a build task — FR-360); any `tools/` entries; any banned content such as `Gravity.Dsl.Compiler.dll`, `Gravity.Dsl.Cli.dll`, or `gravc.dll` (none of those are dependencies of this emitter — FR-300, FR-363). Closes the packaging half of AC-4.1. Marked `[Trait("Category", "Slow")]` so it lives on the slow lane alongside the C# emitter and MsBuild pack tests.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/PackContentTests.cs`.
- **Depends on.** T302.

### T314. Host integration + annotation-ownership tests still pass with the new emitter enrolled
- **Acceptance.** Existing `HostIntegrationTests` and `AnnotationNamespaceOwnershipTests` (Phase 0–3) are run against an `EmitterRegistry` that includes the new `JsonSchemaEmitter` alongside `CSharpEmitter` and the outline sample emitter. No `HOST001` / `HOST002` is raised — the three claimed annotation namespaces (`csharp`, `outline`, `json_schema`) are disjoint per LD-17 and the host's existing FR-052 ownership check is satisfied. Existing assertions about target name uniqueness and supported-AST-version negotiation continue to hold. This task confirms Phase 0–3 host tests do not regress; it does NOT add the AC-4.10 cross-emitter coexistence test, which is a P4c concern (T356) because it asserts a richer end-to-end fixture.
- **Files.** `Gravity.Dsl.Tests/Emitter/HostIntegrationTests.cs` (extend with the JSON Schema emitter instance), `Gravity.Dsl.Tests/Emitter/AnnotationNamespaceOwnershipTests.cs` (extend).
- **Depends on.** T303.

---

## Sub-phase P4b — Schema generation (T320–T332)

Goal: implement every renderer driving every shape declared in LD-15 (entity-state root, per-event payload, per-command request, per-command response, per-state enum, per-value-type file, per-enum file) plus the type-mapping table (FR-330..FR-334) and `@json_schema` annotation folding (FR-340..FR-344). P4b cannot begin until P4a's IEmitter surface is discoverable. By end of P4b the emitter produces a complete file tree against `samples/registry/` with zero diagnostics on the happy path; P4c then locks the bytes and the validator-side guarantees. Closes FR-310..FR-319, FR-330..FR-334, FR-340..FR-344, FR-365; AC-4.5, AC-4.6, AC-4.7, AC-4.8 (annotation folding side), AC-4.12 (annotation-key value-type validation). LD-15, LD-16.

### T320. `TypeMapper.MapPrimitive` — primitives + modifier combinations
- **Acceptance.** `TypeMapper.MapPrimitive(PrimitiveKind)` implements the full FR-330 closed-form table as a pure function: `String → { "type": "string" }`, `Int → { "type": "integer", "minimum": -2147483648, "maximum": 2147483647 }`, `Long → { "type": "integer", "minimum": -9223372036854775808, "maximum": 9223372036854775807 }`, `Decimal → { "type": "string", "format": "decimal" }` (string-encoded — FR-330 row "Decimal" rationale: JSON's `number` is IEEE-754 double and cannot losslessly represent regulatory decimals), `Boolean → { "type": "boolean" }`, `Date → { "type": "string", "format": "date" }`, `DateTime → { "type": "string", "format": "date-time" }`, `UUID → { "type": "string", "format": "uuid" }`. A sibling helper `TypeMapper.ApplyModifiers(JsonObject inner, TypeRef typeRef)` implements FR-331 (`T?` does NOT change the fragment — optionality affects the enclosing object's `required` list only) and FR-332 (`T[]` wraps in `{ "type": "array", "items": <inner> }`; `T?[]` and `T[]` produce identical fragments; `String?[]` drops the inner `?` on `items` with no diagnostic per FR-332's documented asymmetry). Integer constants are written using `long.MinValue` / `int.MinValue` / `int.MaxValue` literals (no string interpolation that could vary by culture). Closes FR-330, FR-331, FR-332 (renderer half).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/TypeMapper.cs` (extend the skeleton from T307).
- **Depends on.** T307.

### T321. `TypeMapper.MapEntityRelation` — `_id` / `_ids` suffix per cardinality
- **Acceptance.** `TypeMapper.MapEntityRelation(RelationDecl)` returns `(PropertyName, Fragment)` per FR-314 step 3: cardinality `one` → `(relation.Name + "_id", { "type": "string", "format": "uuid", "description": "references <Target> by id (<semantic>)" })`, cardinality `many` → `(relation.Name + "_ids", { "type": "array", "items": { "type": "string", "format": "uuid" }, "uniqueItems": true, "description": "references <Target> by id (<semantic>)" })`. When the relation declares no `Semantic` clause, the description is `"references <Target> by id"`. The `_id` / `_ids` suffix is non-overridable in v1 — there is no DSL knob to rename it. The target-entity identity primitive is assumed `UUID` because that is the documented norm in `samples/registry/`; if a future grammar permits non-UUID identities the mapper will need extending, but Phase 4 does not anticipate it. Closes FR-314 (relation half).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/TypeMapper.cs` (extend).
- **Depends on.** T320.

### T322. `TypeMapper.MapNamedType` — within-bundle vs cross-file $ref
- **Acceptance.** `TypeMapper.MapNamedType(NamedTypeRef, ResolvedModel, IReadOnlySet<string> multiVersionFqns)` implements the FR-318 two-tier $ref strategy: when the resolver attached an entity-local definition (currently only the per-command-response indirection on entity-local return types — none in v1 grammar but reserved), produces `{ "$ref": "#/definitions/<Name>" }`; when the resolver attached a namespace-scope `ValueTypeDecl` or `EnumDecl`, produces `{ "$ref": "<Name>.json" }` for same-namespace siblings or `{ "$ref": "../<other-ns-path>/<Name>.json" }` for cross-namespace references resolved through the same dotted-namespace-to-directory mapping as FR-310. Multi-version handling: when `NamedTypeRef.Version` is non-null AND `multiVersionFqns` contains the referenced FQN, the ref target is the `.v<N>.json` variant; when `Version` is null, the ref target is the unqualified `<Name>.json` (Phase 8 FR-126 max-declared-version rule). No absolute URIs. No `$id`. Array of `$ref` types composes as `{ "type": "array", "items": { "$ref": "<Name>.json" } }` per FR-333. Closes FR-318, FR-333.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/TypeMapper.cs` (extend).
- **Depends on.** T320.

### T323. `AnnotationFolder.FoldOntoProperty` — claimed keys + per-key value-type contracts
- **Acceptance.** `AnnotationFolder.FoldOntoProperty(JsonObject fragment, ImmutableArray<AnnotationDecl> annotations, string propertyName, string entityFqn, ImmutableArray<Diagnostic>.Builder diags)` folds every `@json_schema(...)` annotation onto the property's schema fragment. Walks annotations whose `Namespace == "json_schema"`; for each key-value pair:
  - **Claimed keys** (FR-340): `format`, `pattern`, `description`, `examples`, `minLength`, `maxLength`, `minimum`, `maximum`, `multipleOf`. Each is folded onto `fragment` under its keyword name.
  - **Per-key value-type contracts** (FR-341): `format` / `pattern` / `description` require string values; mismatch emits `JS001` naming the offending key, the offending value, the property name, and the entity FQN per FR-365's message template. `minLength` / `maxLength` require integer ≥ 0; mismatch (string, decimal, or negative) emits `JS001`. `minimum` / `maximum` accept integer or decimal. `multipleOf` accepts integer or decimal ≥ 0; `multipleOf: 0` is rejected with `JS001` (Draft-07 forbids it). `examples` accepts string (wrapped into a one-element array per FR-341) or array of strings.
  - **Unknown keys in the claimed namespace** (FR-342): produces `JS001` with message `"unknown json_schema key '<key>'; the json_schema namespace claims {format, pattern, description, examples, minLength, maxLength, minimum, maximum, multipleOf}"`.
  - **Unknown format values** (FR-341 known set: `email`, `uri`, `uuid`, `date`, `date-time`, `time`, `hostname`, `ipv4`, `ipv6`, `regex`, `decimal`): pass through verbatim but emit `JS004` at Warning severity so consumers using newer-draft formats (e.g. `"duration"`) are not blocked.
  Keys are folded into the fragment in their existing slot if the slot is already present (annotation overrides emitter default — but no v1 emitter default carries a claimed key on a primitive fragment, so this is currently a no-op safeguard). `SortedKeyJsonWriter` (T306) handles the final canonical-then-sorted ordering at emission time; the folder does not pre-sort. Closes FR-340, FR-341, FR-342, FR-344 (emitter-side enforcement).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/AnnotationFolder.cs` (extend skeleton from T308).
- **Depends on.** T308, T320.

### T324 [P]. `Render/PropertyRenderer.cs` — type-mapper + annotation-folder composition
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/PropertyRenderer.cs` declares `internal static class PropertyRenderer` with `JsonObject Render(string propertyName, TypeRef typeRef, ImmutableArray<AnnotationDecl> annotations, ResolvedModel model, IReadOnlySet<string> multiVersionFqns, ImmutableArray<Diagnostic>.Builder diags, string entityFqn)`. The composition is: (a) call `TypeMapper.MapPrimitive` or `TypeMapper.MapNamedType` based on `typeRef` shape, (b) apply `TypeMapper.ApplyModifiers` for optional/array modifiers, (c) fold annotations via `AnnotationFolder.FoldOntoProperty`. Returns the final property fragment. `@json_schema` annotations on non-property positions (e.g. a `RelationDecl`'s annotations) are silently ignored per FR-343; this is consistent with the Phase 0–3 behaviour for `@csharp` on non-property positions. The renderer is the single fan-in point for property → schema fragment so future namespace claims can be wired in one place.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/PropertyRenderer.cs`.
- **Depends on.** T320, T322, T323.

### T325 [P]. `Render/LifecycleStateRenderer.cs` — `<EntityName>State` enum in definitions
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/LifecycleStateRenderer.cs` declares `internal static class LifecycleStateRenderer` with `JsonObject Render(LifecycleDecl lifecycle)`. Returns `{ "type": "string", "enum": [ <states in DSL declaration order> ] }`. Enum order is the AST's `LifecycleDecl.States` order (FR-354 — semantic ordering matters; the first state is the implicit initial state per FR-033). An entity with zero declared states produces `{ "type": "string", "enum": [] }` — Draft-07 accepts this as a valid (unsatisfiable) schema; the DSL grammar guarantees at least one state per entity in practice (FR-024) so the empty case is defensive. Closes FR-315 (renderer half).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/LifecycleStateRenderer.cs`.
- **Depends on.** T306.

### T326 [P]. `Render/EventPayloadRenderer.cs` — one definitions entry per event
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/EventPayloadRenderer.cs` declares `internal static class EventPayloadRenderer` with `JsonObject Render(EventDecl ev, string entityFqn, ResolvedModel model, IReadOnlySet<string> multiVersionFqns, ImmutableArray<Diagnostic>.Builder diags)`. Returns the per-event payload schema per FR-311: `{ "title": "<entityFqn>.<eventName>", "type": "object", "properties": { ... payload fields in DSL declaration order, rendered via PropertyRenderer }, "required": [ ... non-optional payload field names sorted ordinally ], "additionalProperties": false }`. Empty payload (`Submitted {};`) produces an empty `properties` object and empty `required` array. The `definitions` key is the bare event name (e.g. `Submitted`), not `Events.Submitted` — the entity-bundle scope disambiguates. Annotations on event payload fields flow through `PropertyRenderer` and fold per FR-340. Closes FR-311.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/EventPayloadRenderer.cs`.
- **Depends on.** T324.

### T327 [P]. `Render/CommandReqRespRenderer.cs` — request + response per command
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/CommandReqRespRenderer.cs` declares `internal static class CommandReqRespRenderer` with `JsonObject RenderRequest(CommandDecl cmd, string entityFqn, ResolvedModel model, IReadOnlySet<string> multiVersionFqns, ImmutableArray<Diagnostic>.Builder diags)` and `JsonObject RenderResponse(CommandDecl cmd, ResolvedModel model, IReadOnlySet<string> multiVersionFqns)`. `RenderRequest` returns `{ "title": "<entityFqn>.<cmd.Name>.Request", "type": "object", "properties": { ... arguments in DSL declaration order via PropertyRenderer }, "required": [ ... non-optional argument names sorted ordinally ], "additionalProperties": false }` per FR-312; zero-argument commands produce empty `properties` and `required`. `RenderResponse` always returns a `$ref` indirection per FR-313: `{ "$ref": "#/definitions/<ReturnsType>" }` when the return type is entity-local (reserved; never the case in v1 grammar), or `{ "$ref": "<ReturnsType>.json" }` when it is namespace-scope. Phase 8's bare-identifier rule on `returns` (no `@N` suffix) is respected — `ReturnsType` is always unversioned at the syntax level, but multi-version coexistence handling still applies through `TypeMapper.MapNamedType` (FR-318). Closes FR-312, FR-313.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/CommandReqRespRenderer.cs`.
- **Depends on.** T322, T324.

### T328 [P]. `Render/ValueTypeRenderer.cs` — standalone file per namespace-scope value type
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/ValueTypeRenderer.cs` declares `internal static class ValueTypeRenderer` with `string Render(ValueTypeDecl vt, ResolvedModel model, DeclKey key, IReadOnlySet<string> multiVersionFqns, ImmutableArray<Diagnostic>.Builder diags)`. Returns a complete stand-alone JSON document per FR-316: `{ "$schema": "http://json-schema.org/draft-07/schema#", "title": "<TypeFQN>", "x-gravity-version": <int>, "type": "object", "properties": { ... fields in DSL declaration order via PropertyRenderer }, "required": [ ... non-optional field names sorted ordinally ], "additionalProperties": false }`. Cross-references to other value types or enums use `$ref` to a sibling file (FR-318 cross-file branch). Document is serialised via `SortedKeyJsonWriter` (T306) with FR-351 canonical ordering. Closes FR-316.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/ValueTypeRenderer.cs`.
- **Depends on.** T306, T324.

### T329 [P]. `Render/EnumRenderer.cs` — standalone file per namespace-scope enum
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/EnumRenderer.cs` declares `internal static class EnumRenderer` with `string Render(EnumDecl en, DeclKey key, IReadOnlySet<string> multiVersionFqns)`. Returns `{ "$schema": "http://json-schema.org/draft-07/schema#", "title": "<EnumFQN>", "x-gravity-version": <int>, "type": "string", "enum": [ ... variants in DSL declaration order ] }` per FR-317. Variant order is NOT sorted (the AC-1 round-trip property requires variant ordering to be observably part of the AST; FR-354 documents this asymmetry with `required` arrays). Serialised via `SortedKeyJsonWriter` (T306) with FR-351 canonical top-level key order. Closes FR-317.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/EnumRenderer.cs`.
- **Depends on.** T306.

### T330. `Render/EntityBundleRenderer.cs` — composes root + all definitions
- **Acceptance.** New file `Gravity.Dsl.Emitter.JsonSchema/Render/EntityBundleRenderer.cs` declares `internal static class EntityBundleRenderer` with `string Render(EntityDecl entity, ResolvedModel model, DeclKey key, IReadOnlySet<string> multiVersionFqns, ImmutableArray<Diagnostic>.Builder diags)`. Builds the per-entity bundle document per FR-310: the root document IS the entity-state schema with `$schema`, `title`, `x-gravity-version`, `type: object`, `properties`, `required`, `additionalProperties: false`, `definitions`. The `properties` map is built in DSL declaration order per FR-314: (1) identity field (typed per FR-330), (2) every `PropertyDecl` (typed + annotations folded via `PropertyRenderer`), (3) every `RelationDecl` (rendered via `TypeMapper.MapEntityRelation`). The reserved `state` slot is added with body `{ "$ref": "#/definitions/<EntityName>State" }` and is in `required`. The `definitions` map carries: one entry per `EventDecl` (via `EventPayloadRenderer`), `<CommandName>Request` + `<CommandName>Response` per `CommandDecl` (via `CommandReqRespRenderer`), `<EntityName>State` (via `LifecycleStateRenderer`). `definitions` keys are sorted ordinally at write time per FR-355. The bundle text is serialised through `SortedKeyJsonWriter`. Closes FR-310, FR-314 (composition half), FR-315 (composition half), FR-319.
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/EntityBundleRenderer.cs`.
- **Depends on.** T306, T324, T325, T326, T327.

### T331. `JsonSchemaEmitter.Emit` — walks model, dispatches to renderers
- **Acceptance.** `JsonSchemaEmitter.Emit(model, config, sink)` body lands per plan.md §3.1 pseudocode: (1) project config via `JsonSchemaEmitterConfig.From`; on `null` (e.g. `JS002` raised), return early with the diagnostic-only `EmitResult`. (2) Compute `multiVersionFqns` via `ComputeMultiVersionFqns(model)` — one pass over `model.Declarations` producing a `HashSet<string>` of FQNs that have more than one Version in scope (FR-310 multi-version filename rule, FR-318 multi-version $ref rule). (3) Build `declToFile` mapping declaration FQN to source file (identical helper shape to `CSharpEmitter:105–122` — duplicated, not linked, per FR-300). (4) Walk `model.Declarations` in the `(FQN ordinal, Version asc)` order pinned by `ResolvedModel.Declarations` being an `ImmutableSortedDictionary` (FR-303, FR-161). Dispatch: `EntityDecl → EntityBundleRenderer.Render`, `ValueTypeDecl → ValueTypeRenderer.Render`, `EnumDecl → EnumRenderer.Render`. Each renderer output is written via `IEmitterOutput.WriteFile` with a `/`-normalised relative path. (5) `BundleFilename(dir, name, version, versioned)` returns `"{name}.v{version}.json"` when `versioned` (FR-310 — the `.v<N>` infix applies to **every** version when more than one version is in scope, symmetric layout) or `"{name}.json"` otherwise. Closes FR-303, FR-310 (filename rule).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/JsonSchemaEmitter.cs` (extend T303 stub).
- **Depends on.** T304, T306, T328, T329, T330.

### T332 [P]. Reserved property collision check (`state` slot collision → JS003)
- **Acceptance.** `EntityBundleRenderer` (T330) detects when a `PropertyDecl` on an `EntityDecl` is named `state` (ordinal string comparison) and emits `JS003` with message `"entity '<entityFqn>' declares a property named 'state' that collides with the reserved entity-state property"` and span `propertyDecl.Span` per FR-365. The colliding property is dropped from the rendered output (not silently overridden by the reserved `state` slot — explicit error so the author notices). The `state` slot itself is still emitted with the `{ "$ref": "#/definitions/<EntityName>State" }` body. Closes FR-315 reservation enforcement (JS003 — FR-364).
- **Files.** `Gravity.Dsl.Emitter.JsonSchema/Render/EntityBundleRenderer.cs` (extend T330).
- **Depends on.** T330.

---

## Sub-phase P4c — Tests, goldens, and integration (T350–T369)

Goal: lock the bytes produced by P4b against byte-checked goldens, validate every emitted file against the Draft-07 metaschema, prove byte-determinism across runs and across packs, and verify cross-emitter coexistence with the C# emitter and outline sample emitter. P4c cannot begin until P4b's renderer set produces a complete file tree for `samples/registry/`. Closes FR-350..FR-355; AC-4.3, AC-4.4, AC-4.9, AC-4.10, AC-4.11.

### T350. `TypeMappingTests.cs` — parameterised over every primitive + modifier combo
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/TypeMappingTests.cs` is parameterised across every row of FR-330 (8 primitives) × every modifier combination from FR-331 / FR-332 (`T`, `T?`, `T[]`, `T?[]`, `T[]?`, `T?[]?` — six combinations). Each parameterised case constructs a minimal fixture entity with a single property of that primitive + modifier, runs the emitter, and byte-compares the relevant property fragment against the expected literal asserted in code (not against the golden — that's T354's job). The `Long` case explicitly asserts `"minimum": -9223372036854775808, "maximum": 9223372036854775807` so a regression that overflows to int32 bounds fails. The `Decimal` case asserts `{ "type": "string", "format": "decimal" }`. The `String?[]` case asserts the inner `?` is dropped on `items` (FR-332's documented asymmetry — `String?[]` and `String[]` produce identical `items` fragments). Closes AC-4.5 (in-code assertions; goldens are byte-checked separately in T354).
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/TypeMappingTests.cs`.
- **Depends on.** T320, T331.

### T351 [P]. `AnnotationFoldingTests.cs` — positive + negative cases
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/AnnotationFoldingTests.cs` covers every claimed `@json_schema` key from FR-340 in a positive fixture: a property declares `@json_schema(format: "email", pattern: "^.+@.+$", description: "Primary email", minLength: 3, maxLength: 254, examples: "user@example.com", minimum: 0, maximum: 100, multipleOf: 0.01)` and the test asserts each key folds correctly onto the property fragment with the right value type per FR-341. The canonical-then-sorted byte order from FR-351 is asserted explicitly: the property's keys appear in the order `type, description, examples, format, maxLength, maximum, minLength, minimum, multipleOf, pattern` after the canonical `type` slot. Negative sub-cases pin AC-4.12: (a) `@json_schema(format: 42)` raises one `JS001` naming the offending key and value type, (b) `@json_schema(minLength: "ten")` raises one `JS001`, (c) `@json_schema(multipleOf: 0)` raises one `JS001` per FR-341's Draft-07-forbids-zero rule, (d) `@json_schema(unknown_key: "x")` raises one `JS001` per FR-342. `JS004` is exercised separately: `@json_schema(format: "ipv6")` succeeds (known format), `@json_schema(format: "duration")` produces `JS004` at Warning severity but does not block emission and the emitted property fragment carries `"format": "duration"` verbatim. Closes AC-4.8 (annotation folding) and AC-4.12 (annotation-key value-type validation).
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/AnnotationFoldingTests.cs`.
- **Depends on.** T323, T331.

### T352. `SchemaValidityTests.cs` — validate every emitted file against Draft-07 metaschema
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/SchemaValidityTests.cs` runs the emitter against `samples/registry/`, captures the 20-file output buffer (AC-4.3 file count: 3 entity bundles + 15 namespace-scope value types + 2 enums), and validates each emitted JSON document against the Draft-07 metaschema (`http://json-schema.org/draft-07/schema#`) using `JsonSchema.Net` (test-project-only dependency from T310). All 20 files pass. Closes AC-4.4 (metaschema half — the instance-validation half is covered by T357). Marked `[Trait("Category", "Slow")]` because metaschema parsing + 20 validations runs longer than the standard unit-test budget.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/SchemaValidityTests.cs`.
- **Depends on.** T310, T331.

### T353. Generate goldens — run the emitter against `samples/registry/` once and hand-review
- **Acceptance.** A developer-driven one-time generation step: run `gravc gen --input samples/registry --output tests/golden/json-schema/registry --emitter json-schema` against the locked Phase 4 emitter. The output produces exactly 20 files (AC-4.3): `tests/golden/json-schema/registry/hr/Employee.json`, `TimeEntry.json`, `Project.json`, `ContactInfo.json`, `ContactMethod.json`, `ContractType.json`, plus the 14 result-type schemas (`OnboardResult.json`, `ActivationResult.json`, `LeaveResult.json`, `ReturnResult.json`, `TerminationResult.json`, `SubmissionResult.json`, `ApprovalResult.json`, `RejectionResult.json`, `PlanResult.json`, `StartResult.json`, `HoldResult.json`, `ResumeResult.json`, `CompleteResult.json`, `CancelResult.json`). Hand-review each file: confirm Draft-07 shape, `additionalProperties: false` on every object schema (FR-319), `x-gravity-version` carries the integer version, `$ref` strategy matches FR-318 (relative cross-file refs to siblings, within-bundle refs to `#/definitions/`), `required` arrays sorted ordinally, `properties` declaration order preserved, `enum` arrays in declaration order. Commit the goldens once review passes. LF line endings only; UTF-8 no BOM verified by raw byte inspection.
- **Files.** `tests/golden/json-schema/registry/hr/Employee.json`, `TimeEntry.json`, `Project.json`, `ContactInfo.json`, `ContactMethod.json`, `ContractType.json`, plus 15 namespace-scope value-type goldens per AC-4.3.
- **Depends on.** T331.

### T354. `GoldenFileTests.cs` — byte-compare + UPDATE_GOLDEN=1 regenerate
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/GoldenFileTests.cs` mirrors the existing `Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs` pattern: runs the JSON Schema emitter against `samples/registry/*.gravity` and byte-compares the output buffer against `tests/golden/json-schema/registry/`. Updating goldens requires `UPDATE_GOLDEN=1` env var (matches the Phase 8 / Phase 9 golden mechanism). Comparison is byte-for-byte at the file level — LF line endings, UTF-8 no BOM, canonical-then-sorted JSON key ordering per FR-351 are all implicitly asserted by byte equality. Failure messages name the first differing offset and the surrounding 32-byte window so regressions are quickly diagnosed. Also includes a parameterised primitive-matrix variant byte-comparing fixtures under `tests/golden/json-schema/primitive-matrix/` (AC-4.5) — one golden per `(primitive, modifier-combination)` pair. Closes AC-4.11.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/GoldenFileTests.cs`, `tests/golden/json-schema/primitive-matrix/*.json`.
- **Depends on.** T350, T353.

### T355 [P]. `DeterminismTests.cs` — twice-in-process byte identity
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/DeterminismTests.cs` runs the JSON Schema emitter twice in a single test method against `samples/registry/`; asserts byte-identical output buffers across the two runs (Phase 0–3 AC-6a equivalent, restated under AC-4.9). A second sub-test runs `dotnet pack Gravity.Dsl.Emitter.JsonSchema -c Release` twice (back-to-back, separate output dirs) and asserts SHA-256 byte-equality of the two `.nupkg` files (FR-361 — `dotnet pack` determinism alongside artifact determinism). Cross-platform byte identity (Linux + macOS CI lanes, AC-6b equivalent) is observed implicitly through CI matrix execution; the test itself does not span runtimes but the artefact is asserted identical on each. Marked `[Trait("Category", "Slow")]` because back-to-back `dotnet pack` is the long pole. Closes AC-4.9.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/DeterminismTests.cs`.
- **Depends on.** T331.

### T356 [P]. Cross-emitter coexistence test (csharp + json-schema + outline)
- **Acceptance.** New test `Gravity.Dsl.Tests/Emitter/JsonSchema/CrossEmitterCoexistenceTests.cs` enrolls `JsonSchemaEmitter`, `CSharpEmitter`, and the outline sample emitter into the same `EmitterRegistry.FromInstances` call. Asserts: (a) zero `HOST002` diagnostics (the three claimed annotation namespaces `csharp`, `outline`, `json_schema` are disjoint per LD-17). (b) Each emitter writes to its own output directory without overlap when configured with distinct `output` paths in a synthetic `.gravity.config`. (c) Validator pass through `Validator.Validate(model, registry.ClaimedAnnotationNamespaces())` against a `samples/registry/`-style fixture that uses `@json_schema(format: "email")` on a property succeeds; the validator does not raise `VAL006` on the `json_schema` namespace because the JSON Schema emitter has claimed it. (d) A negative sub-case: adding `@graphql(...)` to a property in the fixture and asserting `VAL006` fires on the `graphql` namespace because no GraphQL emitter is enrolled (Phase 5 future work). Closes AC-4.10.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/CrossEmitterCoexistenceTests.cs`.
- **Depends on.** T331.

### T357 [P]. Round-trip instance fixture test — validate sample JSON against generated schema
- **Acceptance.** New test method `InstanceValidatesAgainstGeneratedSchema` in `SchemaValidityTests.cs` (T352) takes a sample event payload — e.g. `Submitted { submitted_at: DateTime; }` derived from the `TimeEntry` entity in `samples/registry/` — generates the entity bundle, extracts the `definitions.Submitted` schema, and validates a sample instance JSON (`tests/fixtures/json-schema/instances/TimeEntry.Submitted.instance.json`: `{ "submitted_at": "2026-05-18T12:00:00Z" }`) against it using `JsonSchema.Net`. A negative instance (missing required `submitted_at`, or with `submitted_at: 42` — wrong type) is rejected with a non-empty validation error list. Companion fixtures cover one minimal valid instance per entity bundle: `Employee.instance.json`, `TimeEntry.instance.json`, `Project.instance.json` under `tests/fixtures/json-schema/instances/` (each carries identity + required properties + `state: "<initial>"`). Closes the instance-validation half of AC-4.4.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/SchemaValidityTests.cs` (extend T352), `tests/fixtures/json-schema/instances/Employee.instance.json`, `TimeEntry.instance.json`, `Project.instance.json`, `TimeEntry.Submitted.instance.json`.
- **Depends on.** T352.

### T358 [P]. Update `samples/registry/.gravity.config` with commented-out json-schema emitter section
- **Acceptance.** `samples/registry/.gravity.config` gains a commented-out `# json-schema:` block showing the configuration shape: `# json-schema:` `#   output: gen/json-schema` `#   bundle_strategy: per-entity` (default — included for documentation). The block is commented out so the existing Phase 0–3 / Phase 9 csharp-only smoke flow does not start writing JSON Schema artifacts as a side-effect; downstream tests and tutorials wishing to exercise the JSON Schema emitter uncomment the block. Mirrors the Phase 9 T232 outline-emitter commenting strategy. Documents the configured-and-active form for AI authors and human contributors alike per Principle V.
- **Files.** `samples/registry/.gravity.config`.
- **Depends on.** T304.

### T359 [P]. PackContentTests-style test for the JsonSchema emitter package — extended assertions
- **Acceptance.** Extends the T313 `PackContentTests.cs` with the full positive-and-negative content assertion set: asserts presence of `lib/net9.0/Gravity.Dsl.Emitter.JsonSchema.dll`, the `.nuspec`, `[Content_Types].xml`, and the standard NuGet payload. Asserts absence of `Gravity.Dsl.Compiler.dll`, `Gravity.Dsl.Ast.dll`, `Gravity.Dsl.Emitter.dll`, `Gravity.Dsl.Cli.dll`, `gravc.dll`, `JsonSchema.Net.dll`, and any `tasks/` / `tools/` / `buildTransitive/` entries (FR-300, FR-360, FR-363). Note: T313 enforces the core positive set as the bare AC-4.1 minimum; T359 adds the broader negative-content sweep so future dependency drift cannot smuggle a transitive into the package without a failing test. Both tests share a common `OpenNupkg(path)` helper introduced in this task.
- **Files.** `Gravity.Dsl.Tests/Emitter/JsonSchema/PackContentTests.cs` (extend T313).
- **Depends on.** T313, T355.

---

## Phase gate summary

| Sub-phase | Closing tasks | Spec ACs satisfied |
|---|---|---|
| P4a | T300–T314 | AC-4.1, AC-4.2, AC-4.10 (registration half) |
| P4b | T320–T332 | AC-4.5 (renderer half), AC-4.6, AC-4.7, AC-4.8 (annotation folding), AC-4.12 (annotation-key value-type validation) |
| P4c | T350–T369 | AC-4.3, AC-4.4, AC-4.9, AC-4.10 (cross-emitter half), AC-4.11 |

Cross-phase notes:
- **AC-4.10 (cross-emitter coexistence)** is satisfied across two sub-phases: P4a's T309 / T314 prove the JSON Schema emitter registers cleanly alongside Phase 0–3 emitters without HOST001/HOST002, and P4c's T356 proves the richer three-emitter (`csharp` + `outline` + `json-schema`) coexistence against a fixture exercising `@json_schema` and `@graphql` annotations. The split lets P4a gate before P4b begins while still keeping the full integration assertion close to the goldens it depends on.
- **AC-4.5 (type-mapping table golden)** is similarly split: T350 in P4c runs the in-code parameterised assertions across every primitive × modifier combination, and T354's primitive-matrix golden sub-suite byte-checks the same matrix against the committed goldens under `tests/golden/json-schema/primitive-matrix/`. The in-code assertions catch regressions in the type-mapping logic; the goldens catch regressions in JSON serialisation determinism and key ordering.
- **The `state` reservation (FR-315 / JS003)** is enforced at the renderer layer (T332) rather than at the resolver / validator layer because the reservation is emitter-specific — a future emitter without an entity-state root schema (e.g. an OpenAPI emitter that surfaces state through a different path) does not need the reservation. Keeping JS003 in the emitter preserves the principle that domain-only DSL constructs do not encode emitter-specific surface conflicts.
- **No `Directory.Build.props` carve-out** is required for `Gravity.Dsl.Emitter.JsonSchema` (T312). The banned-APIs analyzer's exempt-projects list remains the same three projects established in Phase 9 (`Gravity.Dsl.Cli`, `Gravity.Dsl.Tests`, `Gravity.Dsl.MsBuild`); the JSON Schema emitter has no legitimate need for `DateTime.UtcNow`, machine identity, or temp-file APIs.
- **No new dependencies on the emitter side** (FR-363). `Gravity.Dsl.Emitter.JsonSchema` depends on the BCL `System.Text.Json` only. `JsonSchema.Net` is a test-only dependency added through `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` with `PrivateAssets="all"` (T310) so it does not leak into the emitter's pack content.
- **`x-gravity-version` vendor extension** (FR-310 / FR-316 / FR-317) is the Phase 8 versioning surface exposed at the JSON Schema layer. Draft-07 tolerates unknown top-level keywords, so consumer validators ignore it without error; downstream tools that understand the prefix can read the per-decl version without re-parsing `.gravity` source. The `x-gravity-` prefix is reserved for Gravity-internal vendor extensions and no other prefix is consumed by this emitter.

## Revision history

- 2026-05-18 — Initial lock. Phase 4 task plan authored against `spec.md` FR-300..FR-365 and `plan.md` sub-phases P4a / P4b / P4c.
- 2026-05-18 — Critic-pass fixes: `$defs` → `definitions` everywhere (Draft-07 compliance). T314 file paths corrected to `Gravity.Dsl.Tests/Emitter/HostIntegrationTests.cs` and `Gravity.Dsl.Tests/Emitter/AnnotationNamespaceOwnershipTests.cs`. T353 file-count enumeration aligned to 14 result types (was inconsistently 15).
