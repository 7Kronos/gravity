# Gravity DSL — Task Plan (Phases 0–3)

**Status:** Locked for implementation
**Date:** 2026-05-17
**Driven by:** `specs/001-gravity-dsl/plan.md`

Conventions:
- Tasks numbered `T###` in execution order. Phase boundaries are hard gates; a phase's tasks complete before the next phase begins.
- `[P]` marks tasks runnable in parallel with peers in the same phase.
- Every task lists: acceptance, files touched, depends-on.

---

## Phase 0 — Spike

### T001. Scaffold solution and projects
- **Acceptance.** `dotnet build` succeeds on an empty solution containing the project skeletons listed in plan.md §2 (`Gravity.Dsl.Ast`, `Gravity.Dsl.Compiler`, `Gravity.Dsl.Emitter`, `Gravity.Dsl.Emitter.CSharp`, `Gravity.Dsl.Cli`, `Gravity.Dsl.Tests`). Target framework `net9.0` everywhere.
- **Files.** `Gravity.Dsl.sln`, `Gravity.Dsl.*/Gravity.Dsl.*.csproj`, `Directory.Build.props`, `global.json`.
- **Depends on.** —

### T002. Pin parser library and NuGet baseline
- **Acceptance.** `Gravity.Dsl.Compiler.csproj` references `Pidgin`. `Gravity.Dsl.Emitter.CSharp.csproj` references `Microsoft.CodeAnalysis.CSharp`. `Gravity.Dsl.Emitter.csproj` references `YamlDotNet`. `Gravity.Dsl.Cli.csproj` references `System.CommandLine`. Versions captured in `Directory.Packages.props` (Central Package Management).
- **Files.** `Directory.Packages.props`, each `*.csproj`.
- **Depends on.** T001.

### T003. Author sample `.gravity` files for `Employee`, `TimeEntry`, `Project`
- **Acceptance.** `samples/registry/Employee.gravity`, `samples/registry/TimeEntry.gravity`, `samples/registry/Project.gravity` cover identity, relations, properties, lifecycle, events, commands per `docs/specs.md` §4.2. `samples/registry/.gravity.yaml` enables only the `csharp` emitter.
- **Files.** `samples/registry/*.gravity`, `samples/registry/.gravity.yaml`.
- **Depends on.** —

### T004. Hand-write expected C# artifacts as goldens-in-waiting
- **Acceptance.** For each sample entity, a hand-written `.cs` file under `tests/golden/csharp/` demonstrates the target shape: sealed records, idiomatic namespaces, file-scoped namespaces, no `partial`/`virtual`, file header per plan.md §3.7. Files compile under `dotnet build` against `net9.0` in a throwaway harness.
- **Files.** `tests/golden/csharp/hr/Employee.cs`, `EmployeeState.cs`, `EmployeeEvents.cs`, `EmployeeCommands.cs`; same set for `TimeEntry`, `Project`; companion `tests/golden/csharp/_compile_check/_compile_check.csproj`.
- **Depends on.** T003.

### T005. Sketch AST records and `AstVersion`
- **Acceptance.** All AST record types listed in plan.md §3.3 declared in `Gravity.Dsl.Ast`. `AstVersion.Value = "1.0.0"`. Package builds and produces a `.nupkg` locally.
- **Files.** `Gravity.Dsl.Ast/*.cs`.
- **Depends on.** T001.

### T006. Throwaway Pidgin grammar prototype
- **Acceptance.** A scratch console app under `spike/PidginPrototype/` parses each `samples/registry/*.gravity` file into the AST records from T005, prints a JSON dump, and exits 0. The spike fails the build if any sample fails to parse. A round-trip through `System.Text.Json` (with sorted property names + `ImmutableSortedDictionary` converters) yields identical text twice in a row. Discrepancies between the proposal's working syntax and what is actually parseable are recorded as a section appended to `specs/001-gravity-dsl/spec.md`.
- **Files.** `spike/PidginPrototype/Program.cs`, `spike/PidginPrototype/PidginPrototype.csproj`.
- **Depends on.** T002, T003, T005.

### T007 [P]. CI scaffold
- **Acceptance.** `.github/workflows/ci.yml` runs `dotnet restore && dotnet build && dotnet test` on Linux and macOS for `net9.0`. No green badge required yet; workflow exists and is wired.
- **Files.** `.github/workflows/ci.yml`.
- **Depends on.** T001.

### T008 [P]. Repository hygiene files
- **Acceptance.** `LICENSE` (Apache 2.0 text, LD-4), `README.md` (one-paragraph project description + link to `docs/specs.md`), `.editorconfig` (four-space C# indent, LF line endings), `.gitignore` (standard .NET).
- **Files.** `LICENSE`, `README.md`, `.editorconfig`, `.gitignore`.
- **Depends on.** —

---

## Phase 1 — Compiler core

### T010. Lexer
- **Acceptance.** `Lexer.Tokenize(source, path)` yields tokens for all reserved words (FR-004), identifiers, primitives, integer and decimal literals, string literals, punctuation (`{ } ( ) ; , : ? [ ] -> @`), and `//` / `/* */` comments. Unknown character produces a `Diagnostic` with rule `LEX001`.
- **Files.** `Gravity.Dsl.Compiler/Lexing/Token.cs`, `TokenKind.cs`, `Lexer.cs`.
- **Depends on.** T005.

### T011. Lexer unit tests
- **Acceptance.** Token-by-token assertions for at least: `entity X version 1 { }`, every reserved word, every primitive, an annotation `@ns(k: "v", n: 3)`, both comment forms, unterminated string error.
- **Files.** `Gravity.Dsl.Tests/Lexing/LexerTests.cs`.
- **Depends on.** T010.

### T012. Parser — top-level + namespace + import
- **Acceptance.** `Parser.Parse(path, source)` produces a `SourceFile` with `NamespaceDecl?`, `ImmutableArray<ImportDecl>`, and an empty `Declarations` array for inputs containing only those. Errors report `path:line:col`.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs`, `Parser.cs`.
- **Depends on.** T010.

### T013 [P]. Parser — value types and enums
- **Acceptance.** Parses every `type` and `enum` declaration in `samples/registry/`. Supports optional `version <int>` clause.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T012.

### T014. Parser — entity skeleton (identity, version, deprecates)
- **Acceptance.** `entity Foo version 1 { identity id: UUID; }` parses to an `EntityDecl` with empty sections. `deprecates version <int> until "<date>"` is captured into `DeprecatesClause`.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T012.

### T015 [P]. Parser — relations block
- **Acceptance.** Parses `name: Type[?] cardinality (one|many) [semantic ident];` lines. AST `RelationDecl` populated correctly.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T014.

### T016 [P]. Parser — properties block with annotations
- **Acceptance.** Parses `name: TypeRef @ns(k: v, ...) ...;` lines. Annotation arguments support string, integer, decimal, boolean, and identifier values. Annotation namespace + name captured into `AnnotationDecl`.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T014.

### T017 [P]. Parser — lifecycle block
- **Acceptance.** Parses `states { A, B, C; }` and `transitions { From -> To on Event; ... }`. AST `LifecycleDecl` populated.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T014.

### T018 [P]. Parser — events block
- **Acceptance.** Parses `EventName { field: Type; ... };` and `EventName {};`. Empty payload is legal.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T014.

### T019 [P]. Parser — commands block
- **Acceptance.** Parses `Name(arg: Type, ...) returns ResponseType with side_effect EventName;`. All three sub-clauses are mandatory per FR-026.
- **Files.** `Gravity.Dsl.Compiler/Parsing/GravityGrammar.cs` (extend).
- **Depends on.** T014.

### T020. Parser unit tests
- **Acceptance.** Per-construct tests for every grammar production. At minimum: namespace + import, value type, enum, entity skeleton, each of the five entity sub-blocks, an end-to-end parse of every file in `samples/registry/`.
- **Files.** `Gravity.Dsl.Tests/Parsing/ParserTests.cs`.
- **Depends on.** T012, T013, T014, T015, T016, T017, T018, T019.

### T021. Source writer (for round-trip)
- **Acceptance.** `SourceWriter.Write(SourceFile)` produces canonical `.gravity` source with the following rules: (a) stable section order inside an entity body — `identity`, `relations`, `properties`, `lifecycle`, `events`, `commands`; (b) four-space indentation; (c) exactly one blank line between top-level declarations and between sections of an entity body; (d) no trailing whitespace on any line; (e) LF line endings only; (f) trailing newline at end of file; (g) comments are dropped (round-trip is AST-level, not text-level); (h) annotation arguments are emitted in `StringComparer.Ordinal` order of key. Re-parsing the output yields a structurally equal `SourceFile` (ignoring `SourceSpan`).
- **Files.** `Gravity.Dsl.Compiler/Parsing/SourceWriter.cs`.
- **Depends on.** T012..T019.

### T022. Round-trip tests (AC-1)
- **Acceptance.** A test fixture loads every `samples/registry/*.gravity` and every `tests/fixtures/parser/*.gravity`, runs parse → write → parse, and asserts AST structural equality. At least 10 fixture files including edge cases (empty events, no relations, multi-deprecates, nested namespaces).
- **Files.** `Gravity.Dsl.Tests/Parsing/RoundTripTests.cs`, `tests/fixtures/parser/*.gravity`.
- **Depends on.** T021.

### T023. Resolver
- **Acceptance.** `Resolver.Resolve(files)` returns a `ResolvedModel` with sorted symbol tables keyed by fully qualified name (`StringComparer.Ordinal`). Resolves imports, detects cycles (rule `RES001`), reports missing-import (`RES002`) and missing-definition (`RES003`) as textually distinct diagnostics. Rule `RES004` rejects multiple in-scope declarations of the same fully-qualified entity name regardless of `version` (Phase 0–3 single-version constraint; FR-042). Rule `RES005` rejects two imported declarations with the same simple name when both are referenced unqualified (FR-063).
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`, `ResolvedModel.cs`, `SymbolTable.cs`.
- **Depends on.** T012..T019.

### T024. Resolver tests
- **Acceptance.** Tests for: simple cross-file import, transitive imports, import cycle, ambiguous import (two files export `Foo`), unresolved entity reference inside `relations`, version disambiguation when two versions of an entity exist.
- **Files.** `Gravity.Dsl.Tests/Resolution/ResolverTests.cs`, `tests/fixtures/resolver/**/*.gravity`.
- **Depends on.** T023.

### T025. Validator
- **Acceptance.** Implements rules FR-030 (`VAL001`), FR-031 (`VAL002`), FR-032 (`VAL003`), FR-033 (`VAL004` warning), FR-021 (`VAL005` warning), FR-051 (`VAL006`), FR-052 (`VAL007`), FR-041 (`VAL009` — deprecates date matches `^\d{4}-\d{2}-\d{2}$` and is a real calendar date), FR-022 (`VAL010` — `?` + `cardinality many` rejected). Each rule emits one diagnostic per violation with span. Note: import cycles, missing imports, missing definitions, duplicate FQNs, and import ambiguity live in the resolver (`RES001..RES005`), not the validator.
- **Files.** `Gravity.Dsl.Compiler/Validation/Validator.cs`, `Validation/Rules/*.cs`, `Validation/RuleIds.cs`.
- **Depends on.** T023.

### T026. Validator tests
- **Acceptance.** One positive and one negative fixture per `VAL00x` rule. Negative fixtures assert exact rule ID + message substring.
- **Files.** `Gravity.Dsl.Tests/Validation/ValidatorTests.cs`, `tests/fixtures/validation/**/*.gravity`.
- **Depends on.** T025.

---

## Phase 2 — AST publication + emitter host

### T030. Publish `Gravity.Dsl.Ast` as a NuGet package
- **Acceptance.** `dotnet pack Gravity.Dsl.Ast` produces a `.nupkg` with `AstVersion.Value = "1.0.0"`, no internal types leaked, README explaining the AST contract and versioning policy.
- **Files.** `Gravity.Dsl.Ast/Gravity.Dsl.Ast.csproj`, `Gravity.Dsl.Ast/README.md`.
- **Depends on.** T005.

### T031. `IEmitter` contract
- **Acceptance.** Interfaces and types per plan.md §3.6 declared in `Gravity.Dsl.Emitter`: `IEmitter`, `IEmitterOutput`, `EmitResult`, `EmitterConfig`, `EmitterConfigSchema`, `SemanticVersionRange`.
- **Files.** `Gravity.Dsl.Emitter/IEmitter.cs`, `IEmitterOutput.cs`, `EmitResult.cs`, `EmitterConfig.cs`, `EmitterConfigSchema.cs`, `SemanticVersionRange.cs`.
- **Depends on.** T030.

### T032. Emitter discovery
- **Acceptance.** `EmitterRegistry.Discover(pluginDirectory)` loads assemblies, finds `IEmitter` exports, refuses emitters whose `SupportedAstVersions` excludes `AstVersion.Value` with rule `HOST001`, returns an immutable registry sorted by `TargetName`.
- **Files.** `Gravity.Dsl.Emitter/EmitterRegistry.cs`.
- **Depends on.** T031.

### T033. Annotation namespace ownership enforcement (FR-052)
- **Acceptance.** Two discovered emitters claiming the same `AnnotationNamespace` produce a single diagnostic with template `HOST002: annotation namespace '<ns>' is claimed by both '<targetA>' and '<targetB>'`, where `<targetA>` and `<targetB>` are sorted by `StringComparer.Ordinal`. Tested with two stub emitters.
- **Files.** `Gravity.Dsl.Emitter/EmitterRegistry.cs` (extend), `Gravity.Dsl.Tests/Emitter/AnnotationNamespaceOwnershipTests.cs`.
- **Depends on.** T032.

### T034. `.gravity.yaml` loader
- **Acceptance.** YAML loader parses the proposal's example config shape into `Dictionary<string, EmitterConfig>`. Unknown top-level keys produce warning `CFG001`. Each emitter section is validated against the emitter's `ConfigurationSchema` (rule `CFG002` for type mismatch, `CFG003` for missing required key).
- **Files.** `Gravity.Dsl.Emitter/ConfigLoader.cs`, `Gravity.Dsl.Tests/Emitter/ConfigLoaderTests.cs`.
- **Depends on.** T031.

### T035. Emitter host + buffered output sink
- **Acceptance.** `EmitterHost.Run(model, configs, registry)` invokes enabled emitters in parallel via `Parallel.ForEachAsync`. Pre-flight: two enabled emitters configured with the same `output` directory abort with `HOST003` naming both target names. `IEmitterOutput` buffers writes in memory, sorts by relative path (`StringComparer.Ordinal`), then commits to a `DirectoryInfo` — commits are create-or-overwrite per file; the host does not delete files outside its own write set. Diagnostics aggregated from emitters are sorted `(Span.Path, Span.Line, Span.Column, RuleId)` before propagation. Same input produces byte-identical on-disk results across runs.
- **Files.** `Gravity.Dsl.Emitter/EmitterHost.cs`, `BufferedEmitterOutput.cs`.
- **Depends on.** T032, T034.

### T036. No-op stub emitter for host tests
- **Acceptance.** `Gravity.Dsl.Tests/Stubs/NoopEmitter.cs` claims `TargetName = "noop"`, `AnnotationNamespace = ""`, supports AST `1.0.0`, writes a single `noop.txt` containing a sorted-by-name list of every `TopLevelDecl` in the model.
- **Files.** `Gravity.Dsl.Tests/Stubs/NoopEmitter.cs`.
- **Depends on.** T031.

### T037. Host end-to-end integration test (AC-4, AC-5, AC-6 minimum surface)
- **Acceptance.** Test loads `samples/registry/`, discovers the stub emitter from a test plugin directory, runs the host, asserts the `noop.txt` output is byte-identical across two runs and across `Linux` + `macOS` CI legs.
- **Files.** `Gravity.Dsl.Tests/Emitter/HostIntegrationTests.cs`.
- **Depends on.** T035, T036.

### T038 [P]. Banned-API analyzer for emitters and host
- **Acceptance.** `Microsoft.CodeAnalysis.BannedApiAnalyzers` is wired in `Directory.Build.props` for `Gravity.Dsl.Compiler`, `Gravity.Dsl.Ast`, `Gravity.Dsl.Emitter`, and every `Gravity.Dsl.Emitter.*` project. `BannedSymbols.txt` blocks `System.DateTime.Now`, `System.DateTime.UtcNow`, `System.DateTimeOffset.Now`, `System.DateTimeOffset.UtcNow`, `System.Guid.NewGuid()`, `System.Environment.MachineName`, `System.Environment.UserName`, `System.IO.Path.GetTempFileName()`, `System.IO.Path.GetTempPath()`, any `System.Random` constructor or static method, `System.String.ToUpper()`, `System.String.ToLower()`, `System.String.Compare(System.String, System.String)`. Build fails on any violation. A negative test asserts that a project with a banned symbol fails to compile.
- **Files.** `BannedSymbols.txt`, `Directory.Build.props` (analyzer reference), `Gravity.Dsl.Tests/Determinism/BannedApiNegativeTests.cs`.
- **Depends on.** T031.

### T039 [P]. Registry-coupling lint
- **Acceptance.** A CI step (PowerShell or bash + `rg`) fails if any source file under `Gravity.Dsl.Compiler/`, `Gravity.Dsl.Ast/`, or `Gravity.Dsl.Emitter/` contains the substrings `scope`, `permission`, `release`, `library`, `registry` (case-insensitive), except for an explicit whitelist file documenting legitimate uses.
- **Files.** `.github/workflows/ci.yml` (step), `scripts/check-no-registry-coupling.sh`, `scripts/registry-coupling-allowlist.txt`.
- **Depends on.** T007.

---

## Phase 3 — C# reference emitter

### T040. C# emitter skeleton
- **Acceptance.** `CSharpEmitter` implements `IEmitter` with `TargetName = "csharp"`, `AnnotationNamespace = "csharp"`, `SupportedAstVersions = ">=1.0.0 <2.0.0"`, configuration schema declares `output` (string, required), `namespace` (string, optional), `file_scoped_namespaces` (bool, default `true`).
- **Files.** `Gravity.Dsl.Emitter.CSharp/CSharpEmitter.cs`, `CSharpEmitterConfig.cs`.
- **Depends on.** T031.

### T041. Value type + enum emission
- **Acceptance.** For every `ValueTypeDecl`, emits a `sealed record` with init-only properties. For every `EnumDecl`, emits a `public enum`. Files written to `<config.output>/<namespace path>/<Name>.cs`.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/ValueTypeRenderer.cs`, `EnumRenderer.cs`.
- **Depends on.** T040.

### T042. Entity state enum emission
- **Acceptance.** `<EntityName>State.cs` declares `public enum <EntityName>State` listing states in DSL declaration order. No additional members.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/StateEnumRenderer.cs`.
- **Depends on.** T040.

### T043. Entity record emission
- **Acceptance.** `<EntityName>.cs` declares `public sealed record <EntityName>` with identity field, properties, and relation reference fields. Relation `cardinality many` becomes `ImmutableArray<TTarget>`; optional becomes nullable reference. No `partial`, no `virtual`.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/EntityRecordRenderer.cs`.
- **Depends on.** T040.

### T044. Event records emission
- **Acceptance.** `<EntityName>Events.cs` declares one `public sealed record` per event, with payload fields. Empty payload records take no parameters.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/EventRecordRenderer.cs`.
- **Depends on.** T040.

### T045. Command records emission
- **Acceptance.** `<EntityName>Commands.cs` declares one `public sealed record` per command with positional arguments matching DSL order. Command's `ReturnsType` and `SideEffectEvent` are surfaced as XML doc comments on the record.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/CommandRecordRenderer.cs`.
- **Depends on.** T040.

### T046. Namespace mapping
- **Acceptance.** DSL `namespace hr;` maps to C# namespace `hr` (or `<config.namespace>.hr` when overridden). File-scoped namespaces used when `file_scoped_namespaces = true`. Nested DSL namespaces map to nested C# namespaces.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/NamespaceMapper.cs`.
- **Depends on.** T040.

### T047. File header + formatter
- **Acceptance.** Every emitted file begins with the fixed three-line header from plan.md §3.7. Roslyn `AdhocWorkspace` is configured for four-space indentation, LF line endings, sorted `using`s, and idiomatic spacing. No timestamps or machine identifiers anywhere.
- **Files.** `Gravity.Dsl.Emitter.CSharp/Emit/FileHeader.cs`, `Emit/RoslynFormatter.cs`.
- **Depends on.** T040.

### T048. Golden-file tests (AC-2)
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs` runs the C# emitter against `samples/registry/` and asserts byte-equality against every file under `tests/golden/csharp/`. The hand-written goldens from T004 become the locked baseline; any mismatch fails the test. Updating goldens requires `dotnet test -- --update-golden` and a deliberate commit.
- **Files.** `Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs`, `tests/golden/csharp/**/*.cs` (locked from T004).
- **Depends on.** T041..T047, T004.

### T049. Determinism test (AC-6)
- **Acceptance.** The golden-file test runs twice in a single test method and asserts byte-identical output. CI runs the same suite on Linux and macOS; outputs match.
- **Files.** `Gravity.Dsl.Tests/Emitter/CSharp/DeterminismTests.cs`, `.github/workflows/ci.yml` (matrix).
- **Depends on.** T048.

### T050. `gravc gen` CLI wiring
- **Acceptance.** `gravc gen --input <dir> --output <dir> [--emitter <name>]*` parses every `*.gravity` file under `--input`, runs resolver + validator + host, writes per-emitter output under `<output>/<emitter>/`. Non-zero exit on any error diagnostic.
- **Files.** `Gravity.Dsl.Cli/Commands/GenCommand.cs`, `Program.cs`.
- **Depends on.** T035, T040.

### T051 [P]. `gravc check` CLI wiring
- **Acceptance.** `gravc check --input <dir>` runs parser + resolver + validator and prints diagnostics in the `path:line:col: <severity> <rule-id>: <message>` format. Exit code 0 if no errors, 1 otherwise.
- **Files.** `Gravity.Dsl.Cli/Commands/CheckCommand.cs`.
- **Depends on.** T050.

### T052. End-to-end smoke test (AC-3)
- **Acceptance.** A test (or CI step) runs `gravc gen --input samples/registry --output out/csharp --emitter csharp`, then runs `dotnet build` against the generated `.cs` files in a throwaway project, and asserts the build succeeds and at least one file per entity is non-empty.
- **Files.** `Gravity.Dsl.Tests/EndToEnd/SmokeTests.cs`, `tests/smoke/_smoke.csproj.tmpl`.
- **Depends on.** T048, T050.

### T053. Error reporting polish (AC-7)
- **Acceptance.** Every diagnostic surface (lexer, parser, resolver, validator, host) emits `path:line:col: <severity> <rule-id>: <message>`. Missing-import (`RES002`) and missing-definition (`RES003`) messages are textually distinct and both name the offending identifier and where it was referenced.
- **Files.** `Gravity.Dsl.Compiler/Diagnostics/DiagnosticFormatter.cs`, updates across rule sites.
- **Depends on.** T010, T012, T023, T025, T035.

---

## Phase gate summary

| Phase | Closing tasks | Spec ACs satisfied |
|---|---|---|
| 0 | T001–T008 | (none; spike outputs) |
| 1 | T010–T026 | AC-1 |
| 2 | T030–T039 | AC-4, AC-5, AC-6a |
| 3 | T040–T053 | AC-2, AC-3, AC-6b, AC-7 |

## Phase 3.5 — Security & correctness pass (2026-05-18)

A targeted pass over Phase 0–3 outputs to close six findings surfaced by the Phase 4 review. Tests are additive; no existing test is deleted, only updated where the previously-passing-but-buggy behaviour changed.

### T060. Resolver — path-traversal rejection (security HIGH, FR-064 / RES006)
- **Acceptance.** `Resolver.Resolve` takes a required `string inputRoot`. Rooted import paths emit `RES006: import 'X' must be a relative path within the input root`. Imports whose canonicalised resolution falls outside `inputRoot` emit `RES006: import 'X' resolves outside the input root '<inputRoot>'`. Both subrules are fatal; the model is null when either fires. `CompilerPipeline.Check` and `Gen` pass `inputRoot` through.
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`, `Gravity.Dsl.Cli/CompilerPipeline.cs`, callers in tests (`Resolver.Resolve` signature change), `Gravity.Dsl.Tests/Resolution/ResolverTests.cs` (new tests: `Import_AbsolutePath_IsRejected_RES006`, `Import_DotDotEscape_IsRejected_RES006`, `Import_Within_InputRoot_IsAccepted`).
- **Depends on.** T023.

### T061. Resolver — RES003 fatal (correctness HIGH)
- **Acceptance.** `RES003` (missing definition) is treated as fatal alongside `RES001`, `RES002`, `RES004`, `RES005`, `RES006`. `Resolver.Resolve` returns `Model = null` so the emitter never runs against an unresolved named type. New test `MissingDefinition_IsFatal_ModelIsNull_RES003` asserts the new behaviour without weakening any existing assertion.
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`, `Gravity.Dsl.Tests/Resolution/ResolverTests.cs`.
- **Depends on.** T023.

### T062. Emitter host — output sandboxing (security HIGH, FR-097 / CFG004)
- **Acceptance.** `EmitterHost.Run` runs a `CFG004` pre-flight before the existing `HOST003` overlap check: rooted `output` values and paths that escape the configured output root (after `Path.GetFullPath` canonicalisation) abort the run with `CFG004: emitter '<target>' output path '<value>' must be a relative path` or `... resolves outside the output root '<outputRoot>'`. New tests in `Gravity.Dsl.Tests/Emitter/EmitterHostSecurityTests.cs`: `Output_AbsolutePath_IsRejected_CFG004`, `Output_DotDotEscape_IsRejected_CFG004`.
- **Files.** `Gravity.Dsl.Emitter/EmitterHost.cs`, `Gravity.Dsl.Emitter/RuleIds.cs`, `Gravity.Dsl.Tests/Emitter/EmitterHostSecurityTests.cs`.
- **Depends on.** T035.

### T063. Buffered output sanitisation (defence-in-depth MEDIUM, FR-098)
- **Acceptance.** `BufferedEmitterOutput.WriteFile` rejects rooted relative paths and any segment equal to `..` with `ArgumentException("relative path required: <input>")`. `CommitTo` re-canonicalises every buffered file path against the output root and refuses to write any file whose canonical path escapes that root. New tests: `WriteFile_AbsolutePath_Throws`, `WriteFile_DotDotSegment_Throws`, `WriteFile_NormalPath_Works`.
- **Files.** `Gravity.Dsl.Emitter/BufferedEmitterOutput.cs`, `Gravity.Dsl.Tests/Emitter/EmitterHostSecurityTests.cs`.
- **Depends on.** T035.

### T064. Lexer unknown-escape diagnostic (correctness HIGH, FR-007 / LEX002)
- **Acceptance.** The lexer emits `LEX002: unknown string escape sequence '\<c>'` for every unsupported escape in a string literal. Recovery advances past both characters, captures the escaped char in the token text, and continues scanning the rest of the literal so spans stay aligned and the parser does not see ghost identifiers. Supported set: `\\`, `\"`, `\n`, `\t`, `\r`. New test `UnknownStringEscape_Emits_LEX002`.
- **Files.** `Gravity.Dsl.Compiler/Lexing/Lexer.cs`, `Gravity.Dsl.Tests/Lexing/LexerTests.cs`.
- **Depends on.** T010.

### T065. Parser depth guard (DoS MEDIUM, FR-006 / PARSE010)
- **Acceptance.** `Parser.Parse` instruments every recursive-descent `Parse*` method with a `try/finally` that increments and decrements an `_depth` counter on the parser state. Past `RuleIds.MaxDepth = 256` the guard throws `PARSE010: maximum nesting depth (256) exceeded` from `EnterDepth`. An internal-only `Parser.ParseWithDepthCap` overload lets the parser test verify the guard via a small cap without breaking real-world inputs. New tests: `DeeplyNestedInput_Emits_PARSE010`, `NaturallyDeepInput_DoesNotTrip_PARSE010_AtProductionCap`.
- **Files.** `Gravity.Dsl.Compiler/Parsing/Parser.cs`, `Gravity.Dsl.Compiler/Parsing/RuleIds.cs` (new), `Gravity.Dsl.Tests/Parsing/ParserTests.cs`.
- **Depends on.** T012.

## Revision history

- 2026-05-17 — Initial lock.
- 2026-05-17 — Critic-pass fixes: T006 spike acceptance now gates on exit-0 + JSON round-trip; T021 SourceWriter spells out canonical form (blank lines, LF, ordinal annotation arg sort, comment-drop); T023 adds `RES004` (duplicate FQN), `RES005` (ambiguous import); T025 adds `VAL009` (deprecates date), `VAL010` (forbid `?` + `cardinality many`), drops `VAL008` (moved to resolver); T033 specifies `HOST002` message template; T035 adds `HOST003` pre-flight + diagnostic sort; T038 extends banned APIs with culture-sensitive string ops + adds negative test. Phase-3 gate now references AC-6a / AC-6b explicitly.
- 2026-05-18 — Phase 3.5 security & correctness pass added: T060 (`RES006` path-traversal rejection), T061 (RES003 fatal), T062 (`CFG004` output sandboxing), T063 (`BufferedEmitterOutput.WriteFile` sanitisation), T064 (`LEX002` unknown-escape diagnostic), T065 (`PARSE010` parser depth guard).
