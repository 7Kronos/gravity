# Gravity DSL — Implementation Plan (Phases 0–3)

**Status:** Locked for implementation
**Date:** 2026-05-17
**Driven by:** `specs/001-gravity-dsl/spec.md` and `CLAUDE.md`

---

## 1. Strategy

Four scoped phases executed sequentially, with Phase 0 acting as the grammar-and-AST stabilization gate before any production code is written.

| Phase | Output | Gate |
|---|---|---|
| 0. Spike | Working-draft grammar; AST sketch; hand-derived C# for `Employee`, `TimeEntry`, `Project`; parser-library decision recorded as LD-1 (Pidgin). | Reviewed grammar doc + AST record sketches + sample `.gravity` files parsed by a throwaway Pidgin prototype. |
| 1. Compiler core | Lexer, parser, AST, resolver, validator. Round-trip tests pass. | AC-1 (spec.md §5) green. |
| 2. AST + emitter host | `Gravity.Dsl.Ast` NuGet package; `IEmitter` contract; plugin discovery; `.gravity.config` loader; parallel invocation; determinism harness. | AC-4, AC-5, AC-6 green for a no-op stub emitter. |
| 3. C# reference emitter | `Gravity.Dsl.Emitter.CSharp`; golden-file tests; `gravc` CLI usable end-to-end on `samples/registry`. | AC-2, AC-3, AC-7 green. |

Out-of-scope phases (4–10) are referenced but not addressed: JSON Schema, GraphQL, OpenAPI, AsyncAPI emitters; additive-only enforcement; MSBuild integration; OSS launch.

## 2. Project layout

Mirrors `docs/specs.md` §6.3 exactly, restricted to packages needed for Phases 0–3.

```
Gravity.Dsl/
├── Gravity.Dsl.Ast/                  # Public AST records + AstVersion (NuGet)
├── Gravity.Dsl.Compiler/             # Lexer, parser, resolver, validator
├── Gravity.Dsl.Emitter/              # IEmitter contract + host + config loader
├── Gravity.Dsl.Emitter.CSharp/       # C# reference emitter (NuGet)
├── Gravity.Dsl.Cli/                  # `gravc` CLI driver
├── Gravity.Dsl.Tests/                # xUnit: round-trip, golden, integration
├── samples/registry/                 # Employee.gravity, TimeEntry.gravity, Project.gravity, .gravity.config
└── tests/golden/csharp/              # Byte-checked expected C# output
```

NuGet package boundaries:

- **`Gravity.Dsl.Ast`** — public, consumed by every emitter.
- **`Gravity.Dsl.Emitter`** — public, declares `IEmitter` and configuration schema base; depends on `Gravity.Dsl.Ast`.
- **`Gravity.Dsl.Emitter.CSharp`** — public reference plugin; depends on `Gravity.Dsl.Ast` + `Gravity.Dsl.Emitter`.
- **`Gravity.Dsl.Compiler`** and **`Gravity.Dsl.Cli`** — distributed as the `gravc` CLI tool (`dotnet tool install`); emitters are not bundled in this package.

Target framework: `net9.0`. Rationale: `net9.0` is the current LTS-adjacent target external users can `dotnet tool install`; `net10.0` is too recent and `net11.0` is preview, neither suitable for a tool intended to be widely consumed.

## 3. Module-level architecture

### 3.1 Lexer (`Gravity.Dsl.Compiler/Lexing`)

- Responsibility: source text → token stream.
- Public API: `internal` to compiler; reused by parser only.
- Key types: `Token` (record: `TokenKind`, `string Lexeme`, `SourceSpan Span`), `TokenKind` (enum), `Lexer` (static `IEnumerable<Token> Tokenize(string source, string path)`).
- Dependencies: none beyond BCL.

### 3.2 Parser (`Gravity.Dsl.Compiler/Parsing`)

- Responsibility: token stream → unresolved AST.
- Public API: `static ParseResult Parse(string path, string source)` returning `ParseResult(SourceFile? File, IReadOnlyList<Diagnostic> Diagnostics)`.
- Built on **Pidgin** (LD-1).
- Key types: combinator grammar in `GravityGrammar.cs`; each top-level construct (entity, type, enum, namespace, import) has a dedicated parser.
- Dependencies: `Pidgin` NuGet, `Gravity.Dsl.Ast`.

### 3.3 AST (`Gravity.Dsl.Ast`)

Public, versioned, immutable. All nodes are `record`s with `init`-only properties.

```csharp
public static class AstVersion
{
    public const string Value = "1.0.0";
}

public sealed record SourceSpan(string Path, int Line, int Column, int Length);

public sealed record SourceFile(
    string Path,
    NamespaceDecl? Namespace,
    ImmutableArray<ImportDecl> Imports,
    ImmutableArray<TopLevelDecl> Declarations);

public sealed record NamespaceDecl(string Name, SourceSpan Span);

public sealed record ImportDecl(string RelativePath, SourceSpan Span);

public abstract record TopLevelDecl(string Name, int Version, SourceSpan Span);

public sealed record EntityDecl(
    string Name,
    int Version,
    DeprecatesClause? Deprecates,
    IdentityDecl Identity,
    ImmutableArray<RelationDecl> Relations,
    ImmutableArray<PropertyDecl> Properties,
    LifecycleDecl Lifecycle,
    ImmutableArray<EventDecl> Events,
    ImmutableArray<CommandDecl> Commands,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span)
    : TopLevelDecl(Name, Version, Span);

public sealed record ValueTypeDecl(
    string Name,
    int Version,
    ImmutableArray<FieldDecl> Fields,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span)
    : TopLevelDecl(Name, Version, Span);

public sealed record EnumDecl(
    string Name,
    int Version,
    ImmutableArray<string> Variants,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span)
    : TopLevelDecl(Name, Version, Span);

public sealed record DeprecatesClause(int Version, string UntilIso8601, SourceSpan Span);

public sealed record IdentityDecl(string FieldName, TypeRef Type, SourceSpan Span);

public sealed record RelationDecl(
    string Name,
    string TargetEntity,
    bool IsOptional,
    Cardinality Cardinality,
    string? Semantic,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);

public enum Cardinality { One, Many }

public sealed record PropertyDecl(
    string Name,
    TypeRef Type,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);

public sealed record FieldDecl(string Name, TypeRef Type, SourceSpan Span);

public sealed record LifecycleDecl(
    ImmutableArray<string> States,
    ImmutableArray<TransitionDecl> Transitions,
    SourceSpan Span);

public sealed record TransitionDecl(string From, string To, string OnEvent, SourceSpan Span);

public sealed record EventDecl(
    string Name,
    ImmutableArray<FieldDecl> Payload,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);

public sealed record CommandDecl(
    string Name,
    ImmutableArray<FieldDecl> Arguments,
    string ReturnsType,
    string SideEffectEvent,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);

public abstract record TypeRef(SourceSpan Span);
public sealed record PrimitiveTypeRef(PrimitiveKind Kind, bool IsOptional, bool IsArray, SourceSpan Span) : TypeRef(Span);
public sealed record NamedTypeRef(string Name, bool IsOptional, bool IsArray, SourceSpan Span) : TypeRef(Span);
public enum PrimitiveKind { String, Int, Long, Decimal, Boolean, Date, DateTime, Uuid }

public sealed record AnnotationDecl(
    string Namespace,
    string Name,
    ImmutableSortedDictionary<string, AnnotationValue> Arguments,
    SourceSpan Span);

public abstract record AnnotationValue;
public sealed record AnnotationStringValue(string Value) : AnnotationValue;
public sealed record AnnotationIntValue(long Value) : AnnotationValue;
public sealed record AnnotationDecimalValue(decimal Value) : AnnotationValue;
public sealed record AnnotationBoolValue(bool Value) : AnnotationValue;
public sealed record AnnotationIdentValue(string Value) : AnnotationValue;

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string RuleId,
    string Message,
    SourceSpan Span);

public enum DiagnosticSeverity { Error, Warning, Info }
```

`ImmutableArray<T>` and `ImmutableSortedDictionary<TK,TV>` are deliberate: they communicate the read-only contract (Principle III) and make iteration order deterministic. `ImmutableDictionary` is never used in AST nodes that emitters consume (its enumeration is hash-bucket order, stable per process but not across architectures).

### 3.4 Resolver (`Gravity.Dsl.Compiler/Resolution`)

- Responsibility: walk parsed `SourceFile`s, resolve imports, build a `ResolvedModel` keyed by fully qualified name, attach version disambiguation.
- Public API: `static ResolveResult Resolve(IReadOnlyList<SourceFile> files, string inputRoot)` returning `ResolveResult(ResolvedModel? Model, IReadOnlyList<Diagnostic> Diagnostics)`. The `inputRoot` parameter is canonicalised via `Path.GetFullPath` once and every resolved import must equal it or live strictly beneath it (ordinal compare on `inputRoot + DirectorySeparatorChar`).
- Key types: `ResolvedModel` (immutable; sorted dictionaries by FQN for determinism), `SymbolTable`.
- Dependencies: `Gravity.Dsl.Ast`.
- **Rule `RES006`.** Rooted import paths and paths that escape `inputRoot` after canonicalisation are rejected. Both subrules are fatal: when emitted the resolver returns `Model = null`. `RES003` (missing definition) is also fatal so emitters never run against an unresolved named type.

### 3.5 Validator (`Gravity.Dsl.Compiler/Validation`)

- Responsibility: enforce FR-030..FR-033, FR-051..FR-052, FR-021 warning, FR-022 (`VAL010` forbids `?` + `cardinality many`), FR-041 (`VAL009` validates the deprecates date string). FR-061 and FR-063 (import/ambiguity) live in the **resolver** (`RES002`/`RES003`/the new `RES004` "multiple in-scope versions of FQN"), not the validator. Phase 0–3 does **not** implement FR-040..FR-042 additive-change enforcement; it records the data only.
- Public API: `static IReadOnlyList<Diagnostic> Validate(ResolvedModel model, EmitterRegistry emitters)`.
- Key types: `ValidationRule` (one per FR), `RuleId` string constants.
- Dependencies: `Gravity.Dsl.Ast`, `Gravity.Dsl.Emitter` (for annotation namespace ownership lookup).

### 3.6 Emitter host (`Gravity.Dsl.Emitter`)

`IEmitter` contract:

```csharp
public interface IEmitter
{
    string TargetName { get; }                       // e.g. "csharp"
    string AnnotationNamespace { get; }              // e.g. "csharp"; "" if none
    SemanticVersionRange SupportedAstVersions { get; } // e.g. ">=1.0.0 <2.0.0"
    EmitterConfigSchema ConfigurationSchema { get; }
    EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink);
}

public interface IEmitterOutput
{
    void WriteFile(string relativePath, string contents);
}

public sealed record EmitResult(IReadOnlyList<Diagnostic> Diagnostics);
```

- `EmitterRegistry` discovers plugins from a configured directory (default `./emitters/`) by loading assemblies, scanning for `IEmitter` exports, and rejecting any emitter whose `SupportedAstVersions` excludes `AstVersion.Value`.
- `EmitterHost.Run(model, config, registry)` invokes enabled emitters in parallel using `Parallel.ForEachAsync`; each emitter writes through `IEmitterOutput`, which buffers writes in memory, sorts them by relative path (ordinal), then commits to disk after the emitter returns. This guarantees stable on-disk ordering (Principle I, FR-083).
- **Pre-flight checks** before parallel invocation. Rule `CFG004` (runs first) rejects emitter `output` values that are rooted or, after canonicalisation against the configured output root, escape it; `HOST003` then catches two enabled emitters configured with the same `output` directory. Output directories must be disjoint. `BufferedEmitterOutput.WriteFile` independently rejects rooted relative paths and `..` segments at the buffer layer (FR-098); `CommitTo` re-canonicalises every buffered path and refuses to write any file whose canonical form escapes the per-emitter output root.
- Diagnostics returned by each emitter are sorted by `(Span.Path, Span.Line, Span.Column, RuleId)` before propagation to the CLI, so reporting order is deterministic regardless of parallel completion order.
- Configuration loader parses `.gravity.config` (YAML via `YamlDotNet`) into `Dictionary<string, EmitterConfig>`; each entry is validated against the corresponding emitter's `ConfigurationSchema`.

### 3.7 C# reference emitter (`Gravity.Dsl.Emitter.CSharp`)

- `TargetName = "csharp"`, `AnnotationNamespace = "csharp"`.
- Emits, per entity `E`:
  - `<E>.cs` — sealed record carrying identity + properties + relation reference types.
  - `<E>State.cs` — `public enum <E>State { ... }` listing lifecycle states in declaration order.
  - `<E>Events.cs` — one sealed record per event with payload fields.
  - `<E>Commands.cs` — one sealed record per command with arguments; one `partial` is **not** used (FR-093).
- Emits one `.cs` file per `type` and `enum` declaration, mirroring DSL namespace as the C# namespace.
- Uses `Microsoft.CodeAnalysis.CSharp` (Roslyn) syntax APIs to build trees, then formats with a fixed `AdhocWorkspace` configured for four-space indentation and file-scoped namespaces (per FR-094, FR-096).
- File header is a fixed string:

  ```
  // <auto-generated>
  //     This file was generated by Gravity DSL.
  //     Source: <relative .gravity path>
  // </auto-generated>
  ```

  No timestamp, no machine identifier, no version of the emitter (versioning lives in the AST package). The header text is sorted into a constant.

### 3.8 CLI (`Gravity.Dsl.Cli`)

- Binary: `gravc` (LD-3).
- Commands (Phase 3 surface):
  - `gravc gen --input <dir> --output <dir> [--emitter <name>]*` — parses every `*.gravity` under `--input`, runs the host, writes per-emitter output under `<output>/<emitter>/`.
  - `gravc check --input <dir>` — parses + resolves + validates; exits non-zero on any error diagnostic.
- Argument parser: `System.CommandLine` (BCL-adjacent, no third-party dependency for CLI surface).
- Diagnostic formatting: `path:line:col: <severity> <rule-id>: <message>`.

## 4. Determinism strategy

Required by FR-083 and Principle I.

- AST collections use `ImmutableArray<T>` for ordered sequences and `ImmutableSortedDictionary<string, TV>` (with `StringComparer.Ordinal`) wherever map ordering matters for emitter output (e.g. `AnnotationDecl.Arguments`). `ImmutableDictionary<TK,TV>` is **not used** in any node consumed by an emitter, because its enumeration order is hash-bucket order — stable per process but not across architectures.
- `ResolvedModel` exposes only sorted enumerations (sorted by FQN, ordinal).
- Emitter output is buffered in `IEmitterOutput`, sorted by relative path (ordinal), then written.
- The C# emitter renders members in DSL declaration order; the Roslyn formatter is invoked with explicit options and a fixed `CultureInfo.InvariantCulture` is set on the host's current thread at startup. **Line endings are always LF** — no per-platform normalization, no `Environment.NewLine`. The C# emitter writes files via a single `StreamWriter` with `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` and `NewLine = "\n"`.
- Banned APIs (enforced by `Microsoft.CodeAnalysis.BannedApiAnalyzers` + `BannedSymbols.txt`, CI-failing) in `Gravity.Dsl.Compiler`, `Gravity.Dsl.Ast`, `Gravity.Dsl.Emitter`, and every `Gravity.Dsl.Emitter.*` project:
    - `System.DateTime.Now`, `System.DateTime.UtcNow`, `System.DateTimeOffset.Now`, `System.DateTimeOffset.UtcNow`
    - `System.Guid.NewGuid()`
    - `System.Environment.MachineName`, `System.Environment.UserName`
    - `System.IO.Path.GetTempFileName()`, `System.IO.Path.GetTempPath()`
    - `System.Random` (any constructor or static method)
    - `System.String.ToUpper()` (use `ToUpperInvariant`), `System.String.ToLower()` (use `ToLowerInvariant`)
    - Culture-sensitive `string.Compare(string, string)` overloads without a `StringComparison` argument
    - Culture-sensitive `decimal.ToString()`, `double.ToString()`, `float.ToString()`, `int.ToString()` without an explicit `IFormatProvider` (the analyzer is permissive on `ToString("X")` style literal-format overloads and tightens only on the parameterless form).
- Two CI legs run identical inputs on Linux + macOS; outputs are byte-compared (AC-6a, AC-6b).

## 5. Test strategy

Three CI tiers per `CLAUDE.md` Quality standards.

### 5.1 Round-trip tests

For every `.gravity` file under `samples/registry/` and a curated `tests/fixtures/parser/` set:

1. Parse to AST `A1`.
2. Serialize `A1` back to canonical `.gravity` source via a `SourceWriter` (Phase 1 utility).
3. Re-parse the serialized text to AST `A2`.
4. Assert `A1` and `A2` are structurally equal (ignoring `SourceSpan`).

### 5.2 Golden-file tests

`tests/golden/csharp/` contains expected C# output for each entity in `samples/registry/`. The test harness runs the C# emitter against `samples/registry/` and compares byte-for-byte. Updating goldens requires a deliberate `--update-golden` flag and a code review.

### 5.3 Cross-emitter integration

In Phases 0–3 only the C# emitter exists, so this tier reduces to: a no-op stub emitter included in `Gravity.Dsl.Tests` exercises the host end-to-end (discovery → config validation → invocation → output buffering) and asserts ordering, determinism, and annotation-namespace ownership (FR-052). When Phase 4 begins, the matrix expands.

## 6. Risk register (Phases 0–3 surface)

| Risk (per proposal §8) | Surface in Phases 0–3 | Mitigation |
|---|---|---|
| Grammar churn | Phase 0 spike likely surfaces ambiguities in `cardinality` / `semantic` / annotation parsing. | Treat Phase 0 grammar as draft; record changes in `specs/001-gravity-dsl/spec.md` revision history before Phase 1 starts. |
| Generated code quality | C# emitter is the only reference; bad output here sets the bar low for community emitters. | FR-094 + manual reviewer pass on every golden update + Roslyn analyzer enforces no `partial`, no `virtual`. |
| AST premature ossification | `Gravity.Dsl.Ast` v1.0.0 ships at end of Phase 2. | `AstVersion` is a public constant; the resolver does not depend on internal-only types; deprecation policy documented in the package README. |
| Versioning model tested late | Phase 8 enforcement is out of scope, but FR-040..FR-042 require the data to be in the AST now. | AST carries `Version` and `DeprecatesClause` from day one; validator is structured around `RuleId` so adding Phase 8 rules is additive. |
| Coupling drift with Registry | None expected in Phases 0–3, but worth a guard. | A CI lint rejects any string containing `scope`, `permission`, `release`, `library`, `registry` in `Gravity.Dsl.Compiler`, `Gravity.Dsl.Ast`, or `Gravity.Dsl.Emitter` source (whitelist for legitimate uses). |
| Pidgin abandonment | LD-1 locks Pidgin as the parser library; if upstream stalls, the compiler is stuck on an old version. | The parser layer is isolated behind `Gravity.Dsl.Compiler/Parsing`; a hand-rolled or Superpower replacement is a 1–2 week swap. AST shape is parser-agnostic. |
| Roslyn formatter version drift | `Microsoft.CodeAnalysis.CSharp.Workspaces` formatter output is sensitive to minor-version changes and host culture. | Pin `Microsoft.CodeAnalysis.CSharp.Workspaces` to a specific minor version in `Directory.Packages.props`; force `CultureInfo.InvariantCulture` at host startup; the golden-file harness is the regression net. |
| Untrusted plugin loading | `EmitterRegistry.Discover` loads arbitrary assemblies from the configured plugin directory; no signature check or sandboxing. | Acceptable for Phase 0–3 (compile-time tool run by the project owner). Plugin-signing and sandboxing are deferred to Phase 9 / OSS launch. |
| Untrusted source files | Adversarial `.gravity` inputs could escape the input root via `import` (`/etc/passwd`, `../../etc/hosts`), force the emitter host to write outside the output root, exhaust the stack via deeply nested constructs, or smuggle data through unknown string escapes. | FR-006 caps parser depth at 256 (`PARSE010`); FR-007 rejects unknown lexer escapes (`LEX002`); FR-064 / `RES006` reject rooted or escaping import paths; FR-097 / `CFG004` reject rooted or escaping emitter `output` paths; FR-098 sanitises `IEmitterOutput.WriteFile` arguments and re-canonicalises every buffered file at commit time. |
| Emitter sprawl | Premature, no risk surface in Phases 0–3. | Emitter authoring guide deferred to Phase 9. |

## 7. Out-of-scope acknowledgements

Documented here so they do not creep in:

- Phases 4–7: JSON Schema, GraphQL, OpenAPI, AsyncAPI emitters. Each will add to `tests/golden/<target>/` and extend the cross-emitter integration tier.
- Phase 8: additive-only enforcement. Data is in the AST today; rules are deferred.
- Phase 9: MSBuild target, CLI ergonomics polish, emitter authoring guide.
- Phase 10: OSS launch — NuGet publication, docs site, contribution guide, sample registry repo.
- All Registry concerns per Principle VII: scopes, permissions, rules, releases, library imports. Not addressed in any phase of this plan.
