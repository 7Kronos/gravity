# Gravity DSL — Specification (Phases 0–3)

**Status:** Locked for implementation
**Date:** 2026-05-17
**Successor of:** `docs/specs.md` (v1 Proposal, 309 lines)
**Scope statement:** This document is the lockable subset of the v1 proposal covering Phases 0–3 of §7: spike, compiler core, AST publication and emitter host, and C# reference emitter. Phases 4–10 (JSON Schema, GraphQL, OpenAPI, AsyncAPI emitters, additive-only enforcement, build integration, OSS launch) are acknowledged as future work and are explicitly out of scope here. The full v1 vision lives in `docs/specs.md` §4, §5, §6.

---

## 1. Goals (tied to constitution)

| Goal | Constitution principle |
|---|---|
| G-1. Every Phase 0–3 output (C# records, AST artifacts) is generated from `.gravity` source; no hand-authored target surface. | I. The DSL is the spec. |
| G-2. Grammar declares meaning only: identity, relations, properties, lifecycle, events, commands, value types, enums. No storage, transport, or deployment vocabulary. | II. Domain-only. |
| G-3. Generated artifacts carry no extension points that allow downstream code to redefine domain meaning. | III. Read-only at build time. |
| G-4. AST and validator carry per-entity version metadata from day one, even though enforcement ships in Phase 8. | IV. Additive-only versioning by default. |
| G-5. Grammar is parseable and writable by current LLMs; sample `Employee`, `TimeEntry`, `Project` files double as LLM legibility fixtures. | V. AI-readable. |
| G-6. C# emitter is one plugin among many; the AST and `IEmitter` contract are stable, NuGet-published, and version-pinned. | VI. Pluggable, not prescriptive. |
| G-7. The DSL compiles and emits without any Registry artifact present; no scope, permission, release, or library construct appears in grammar or AST. | VII. Composable upward. |

## 2. Non-goals (Phases 0–3)

- NG-1. JSON Schema, GraphQL, OpenAPI, AsyncAPI emitters (Phases 4–7).
- NG-2. Additive-only breaking-change diagnostics (Phase 8). Version syntax is parsed and stored in the AST; enforcement is not implemented.
- NG-3. MSBuild target and source generator (Phase 9). CLI only.
- NG-4. LSP, formatter, linter, runtime DSL evaluation (constitution out-of-scope discipline).
- NG-5. Postgres DDL, storage backends, broker bindings (per Principle II).
- NG-6. Registry features — scopes, permissions, rules, releases, library imports (per Principle VII).
- NG-7. AI authoring tooling (separate proposal).

## 3. Locked defaults (proposal §9, user-approved)

| Decision | Value | One-line rationale |
|---|---|---|
| LD-1. Parser library | **Pidgin** | Active maintenance, monadic combinator style, good performance, simpler than Sprache/Superpower for this grammar size. |
| LD-2. File extension | **`.gravity`** | Most explicit, matches the proposal's working assumption, no collision concerns. |
| LD-3. CLI name | **`gravc`** | Gravity compiler — short, clearly scoped, leaves room for a separate Registry CLI. |
| LD-4. License | **Apache 2.0** | Conservative default for a standards-aspirational project; provides a patent grant (Principle VI alignment). |

## 4. Functional requirements

Levels: **MUST**, **SHOULD**, **MAY**. Each requirement traces to a constitution principle (Roman numeral).

### 4.1 Grammar surface

- **FR-001 (MUST, I, V).** A `.gravity` source file consists of an optional `namespace` declaration, zero or more `import` declarations, and one or more top-level declarations: `entity`, `type` (value type), or `enum`.
- **FR-002 (MUST, V).** Comments use `//` (line) and `/* */` (block). Whitespace is insignificant except as token separator.
- **FR-003 (MUST, II).** The grammar contains no keywords or constructs referring to storage, persistence, transport, broker, deployment, scope, permission, release, or library.
- **FR-004 (SHOULD, V).** Identifiers are ASCII letters, digits, and underscore; must begin with a letter. Reserved words: `namespace`, `import`, `entity`, `type`, `enum`, `version`, `deprecates`, `until`, `identity`, `relations`, `properties`, `lifecycle`, `states`, `transitions`, `on`, `events`, `commands`, `returns`, `with`, `side_effect`, `cardinality`, `semantic`.
- **FR-005 (MUST, I).** Source files use the `.gravity` extension (LD-2).
- **FR-006 (MUST, V).** The parser enforces a maximum nesting depth of 256 levels; deeper input emits `PARSE010` and stops parsing the current production. This is a defence-in-depth guard against adversarial inputs causing stack overflow in the recursive-descent driver.
- **FR-007 (MUST, V).** The lexer rejects unknown string escape sequences with `LEX002`; the supported set is `\\, \", \n, \t, \r`. Recovery preserves the escaped character so source spans stay aligned, but the diagnostic flags every offending escape.

### 4.2 Type system

- **FR-010 (MUST, II).** Primitive types: `String`, `Int`, `Long`, `Decimal`, `Boolean`, `Date`, `DateTime`, `UUID`. No others in Phase 0–3.
- **FR-011 (MUST, II).** Optionality is expressed by the `?` suffix on a type reference. Arrays by the `[]` suffix. Both modifiers may apply: `String[]?` and `String?[]` are distinct and both legal.
- **FR-012 (MUST, II).** User-defined `type` blocks declare structured value types as an ordered list of `name: TypeRef;` fields.
- **FR-013 (MUST, II).** User-defined `enum` blocks declare a comma-separated list of identifier variants.
- **FR-014 (SHOULD, II).** Value types and enums carry an optional `version <int>` clause; defaults to `1`. Recorded on the AST node for Phase 8 use.

### 4.3 Entity declaration

- **FR-020 (MUST, II).** An `entity` declares: a name, a version (mandatory), an optional `deprecates` clause, and a body with sections `identity`, `relations`, `properties`, `lifecycle`, `events`, `commands`. Sections may appear in any order; each may appear at most once.
- **FR-021 (MUST, II).** `identity` declares exactly one field `name: TypeRef;`. The type SHOULD be `UUID` but the compiler accepts any primitive in Phase 0–3 (validator warns but does not reject non-`UUID` identity).
- **FR-022 (MUST, II).** `relations` declares zero or more relation lines: `name: EntityName[?] cardinality (one|many) [semantic identifier];`. The optional `semantic` clause assigns a domain role to the relation. Relation targets are single entity names, optionally `?`-suffixed; the `[]` array suffix is not legal on relation targets — use `cardinality many` instead. The combination of `?` and `cardinality many` is **forbidden** (validator rule `VAL010`); an empty `many` relation is represented by an empty collection, never by null.
- **FR-023 (MUST, II).** `properties` declares zero or more `name: TypeRef [annotation*];` lines.
- **FR-024 (MUST, II).** `lifecycle` contains a `states { ... }` block and a `transitions { ... }` block. The `states` block holds a comma-separated list of state identifiers; a trailing comma is legal; the list is terminated by a mandatory `;` inside the block. The `transitions` block holds `From -> To on EventName;` lines, one per line. State identifiers and event identifiers occupy **disjoint name spaces per entity**: a state and an event may share a textual name (e.g. `Submitted`) without collision, because the C# emitter renders them into distinct types (state enum `<Entity>State` vs event record `<EventName>`).
- **FR-025 (MUST, II).** `events` declares zero or more named events with an inline payload record: `EventName { field: TypeRef; ... };`. Empty payloads are written `EventName {};`.
- **FR-026 (MUST, II).** `commands` declares zero or more named commands: `CommandName(arg: TypeRef, ...) returns ResponseTypeName with side_effect EventName;`. The `with side_effect` clause is mandatory in Phase 0–3 to keep the event/command pairing explicit.
- **FR-027 (MUST, I).** Command response types (e.g. `ApprovalResult`) are user-declared via `type` blocks; no implicit generation in Phase 0–3.

### 4.4 Lifecycle semantics

- **FR-030 (MUST, II).** Every state named in a `transitions` line must appear in the `states` block of the same entity.
- **FR-031 (MUST, II).** Every event named in a transition's `on` clause must appear in the entity's `events` block.
- **FR-032 (MUST, II).** Every event named in a command's `with side_effect` clause must appear in the entity's `events` block.
- **FR-033 (SHOULD, II).** The validator emits a warning if a declared state has no incoming transition (other than the implicit initial state, which is the first state listed).

### 4.5 Versioning surface

- **FR-040 (MUST, IV).** Every entity carries a `version <positive int>` clause. AST node records the integer.
- **FR-041 (MUST, IV).** A `deprecates version <int> until "<ISO-8601 date>"` clause is parseable and stored on the AST. Phase 0–3 does not enforce the deprecation window (NG-2); Phase 8 will. The date string is validated by the validator (rule `VAL009`) to match `^\d{4}-\d{2}-\d{2}$` and be a real calendar date.
- **FR-042 (MUST, IV).** Phase 0–3 **disallows multiple in-scope versions of the same fully-qualified entity name**: if the resolver sees two declarations of the same FQN (regardless of `version`), it emits error `RES004`. Version-qualified type references (`Foo@2`) are deferred to Phase 8 along with additive-only enforcement. The `version` integer is recorded on every entity AST node so Phase 8 can introduce the syntax without breaking the AST contract.

### 4.6 Annotations

- **FR-050 (MUST, VI).** Annotations on property fields use the form `@namespace(key: value, ...)` where keys are identifiers and values are string, integer, decimal, boolean, or identifier literals.
- **FR-051 (MUST, VI).** The compiler validates that every annotation `namespace` is claimed by exactly one registered emitter. Unclaimed namespaces produce an error naming the annotation site.
- **FR-052 (MUST, VI).** Two emitters claiming the same namespace produce an emitter-host startup error naming both claimants.
- **FR-053 (MAY, VI).** Annotations on entity, event, command, value type, enum, and relation nodes are reserved syntactically for future use; Phase 0–3 parses and stores them on the AST but no reference emitter consumes them.

### 4.7 Imports and namespaces

- **FR-060 (MUST, II).** `namespace <dotted.identifier>;` is optional. If absent, the file's namespace is the empty namespace.
- **FR-061 (MUST, II).** `import "<relative path>.gravity";` makes all top-level declarations from the imported file visible by simple name in the importing file.
- **FR-062 (MUST, II).** Import cycles are detected by the resolver and rejected with a diagnostic naming the cycle.
- **FR-063 (MUST, II).** Two imported declarations with the same simple name produce an ambiguity error at first use; the user resolves by referring to the fully qualified name.
- **FR-064 (MUST, II).** `import` paths must be relative and resolve within the configured input root; rooted paths and parent-directory escapes are rejected by the resolver with `RES006`. Both checks run before the file-existence check (`RES002`) and treat the violation as fatal so emitters never run against an unresolved file.

### 4.8 AST publication

- **FR-070 (MUST, VI).** The compiler exposes the resolved AST through a public, NuGet-published interface package `Gravity.Dsl.Ast`. AST node types are C# `record`s with `init`-only properties.
- **FR-071 (MUST, VI).** The AST package declares an `AstVersion` constant. Phase 0–3 ships `AstVersion = "1.0.0"`.
- **FR-072 (MUST, VI).** Emitters declare the AST version range they support; the emitter host refuses incompatible emitters with a clear error.

### 4.9 Emitter host

- **FR-080 (MUST, VI).** The emitter host discovers emitters by scanning a configured plugin directory and loading assemblies that export at least one `IEmitter` implementation.
- **FR-081 (MUST, VI).** The host loads emitter configuration from `.gravity.yaml` (YAML). Each emitter's section is validated against the emitter's published configuration schema.
- **FR-082 (MUST, VI).** Enabled emitters are invoked in parallel; output paths are written under each emitter's configured `output` directory.
- **FR-083 (MUST, I, VI).** Every reference emitter writes byte-identical output for identical input across runs and across platforms.
- **FR-097 (MUST, VI).** Emitter `output` paths must be relative and resolve within the configured output root; rooted paths and parent-directory escapes are rejected by the host pre-flight with `CFG004`. The pre-flight runs before the `HOST003` overlapping-output check so a single root-escape produces one targeted diagnostic.
- **FR-098 (MUST, VI).** `IEmitterOutput.WriteFile` rejects rooted relative paths and parent-directory segments with `ArgumentException`; this is defence-in-depth against third-party emitters and is enforced inside the buffered output sink rather than at the host layer.

### 4.10 C# reference emitter (Phase 3)

- **FR-090 (MUST, I).** Emits one `.cs` file per entity, value type, and enum, plus per-entity event records, command records, and command response records.
- **FR-091 (MUST, I).** Generated C# uses `record` types with positional or init-only members and `EqualityContract` defaults. State enums use `enum`.
- **FR-092 (MUST, II).** Generated namespace mirrors the DSL namespace; nested DSL namespaces map to nested C# namespaces.
- **FR-093 (MUST, III).** Generated types are `sealed` where the language allows; no `partial` declarations and no `virtual` members; downstream code cannot redefine domain shape.
- **FR-094 (MUST, V, VI).** Output is human-readable: four-space indentation, XML doc comments derived from DSL identifiers, idiomatic `using` directives, no `// auto-generated` machinery beyond a single deterministic file header (no timestamp).
- **FR-095 (MUST, VI).** The emitter claims annotation namespace `csharp` for future use. Phase 0–3 reads no annotations under it; FR-051 validates the claim is unique.
- **FR-096 (MUST, VI).** Configuration schema: `output` (string, required), `namespace` (string, optional; overrides DSL namespace as prefix), `file_scoped_namespaces` (bool, default `true`).

## 5. Acceptance criteria (Phases 0–3 "done")

- **AC-1.** Round-trip property: every valid source file under `samples/registry/` parses to an AST that re-emits to source that re-parses to the same AST (modulo whitespace and comment positions).
- **AC-2.** Golden-file tests: byte-exact `.cs` files under `tests/golden/csharp/` for `Employee`, `TimeEntry`, `Project`, including event/command/response records and state enums.
- **AC-3.** End-to-end smoke: `gravc gen --input samples/registry --output gen/csharp --emitter csharp` exits 0, produces non-empty `.cs` files, and those files compile under `dotnet build` against `net9.0`.
- **AC-4.** `Gravity.Dsl.Ast` is packaged as a NuGet package with `AstVersion = "1.0.0"`; an external assembly can implement `IEmitter` against it without referencing `Gravity.Dsl.Compiler`.
- **AC-5.** Annotation namespace ownership is enforced: a test with two emitter stubs claiming `csharp` fails host startup with both claimant names in the diagnostic.
- **AC-6a.** Host determinism: the no-op stub emitter, run twice in a single process against `samples/registry/`, produces byte-identical output buffers; the same suite on a second OS image in CI produces byte-identical output.
- **AC-6b.** C# emitter determinism: the golden-file test (AC-2) runs twice in a single process and produces byte-identical files; the same suite on a second OS image in CI produces byte-identical files.
- **AC-7.** Error reporting: every diagnostic carries file path, line, column, rule ID, and a message; missing-import and missing-definition diagnostics are textually distinct.

### 5.1 Revision history

- 2026-05-17 — Initial lock.
- 2026-05-17 — Critic-pass fixes: FR-022 forbids `?` + `cardinality many` and bans `[]` on relation targets (new `VAL010`); FR-024 clarifies state/event name-space disjointness and `states` block terminator; FR-041 adds calendar-date validation (`VAL009`); FR-042 narrowed — multiple in-scope versions of the same FQN rejected by resolver (`RES004`); AC-6 split into AC-6a / AC-6b.
- 2026-05-18 — Security & correctness pass: FR-064 (`RES006` rejects rooted/escaping import paths); FR-097 (`CFG004` rejects rooted/escaping emitter `output` values at host pre-flight); FR-098 (`IEmitterOutput.WriteFile` rejects rooted paths and `..` segments); FR-006 (parser depth guard fires `PARSE010` at 256 nesting levels); FR-007 (lexer rejects unknown string escapes with `LEX002`). RES003 (missing definition) is now fatal alongside the other resolver fatals so the emitter never runs against an unresolved name.

## 6. Cross-references

This artifact locks the surface needed for Phases 0–3. For the full v1 vision — JSON Schema, GraphQL, OpenAPI, AsyncAPI emitters, MSBuild integration, OSS launch — see `docs/specs.md` §4 (DSL syntax full surface), §5 (Pluggable emitter architecture across all five reference targets), §6 (Architecture and build integration). The seven principles governing every decision in this document live in `CLAUDE.md`.
