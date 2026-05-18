# Gravity DSL — Task Plan (Phase 8: Additive-only versioning enforcement)

**Status:** Locked for implementation
**Date:** 2026-05-18
**Driven by:** `specs/002-phase-8-versioning/plan.md`
**Predecessor:** `specs/001-gravity-dsl/tasks.md` (T001..T065). Phase 8 task ids begin at `T100` to keep the global task-id namespace flat and grep-friendly.

Conventions:
- Tasks numbered `T1##` in execution order. Sub-phase boundaries (P8a → P8b → P8c → P8d) are hard gates; a sub-phase's tasks complete before the next sub-phase begins.
- `[P]` marks tasks runnable in parallel with peers in the **same** sub-phase.
- Every task lists: **Acceptance** (verifiable; cites FR/AC from `spec.md`), **Files** (repo-relative paths touched), **Depends on** (prior T-numbers or `—`).
- Phase 0–3 tasks (T001..T065) remain locked. No Phase 8 task removes or weakens a Phase 0–3 acceptance condition; where a Phase 0–3 surface is extended, the extension is additive and the predecessor acceptance still holds.

---

## Sub-phase P8a — Grammar + AST (T100–T111)

Goal: extend `NamedTypeRef` with an additive `int? Version` slot, teach the parser the `@N` suffix, and bump `AstVersion` to `1.1.0`. Zero semantic effect on Phase 0–3 inputs (any source that does not use `@N` parses to the same AST shape it did before). Closes FR-100, FR-101, FR-102, FR-110, FR-111, FR-112, FR-113, FR-151 (`PARSE020`). Closes AC-8.12, AC-8.14, AC-8.16.

### T100. Bump `AstVersion.Value` to `"1.1.0"` and amend the package README
- **Acceptance.** `Gravity.Dsl.Ast/AstVersion.cs` exposes `AstVersion.Value == "1.1.0"`. `Gravity.Dsl.Ast/README.md` gains a "1.1.0 (Phase 8)" section that names the single additive change (`NamedTypeRef` gains an optional `int? Version`), reiterates the FR-111 compatibility promise (a consumer compiled against `1.0.0` continues to function; the new positional argument is documented as additive-only), and points readers at FR-110..FR-113. The bump is the only change in this task; behaviour-touching parser/resolver changes ship under T101..T111.
- **Files.** `Gravity.Dsl.Ast/AstVersion.cs`, `Gravity.Dsl.Ast/README.md`.
- **Depends on.** —

### T101 [P]. Add `int? Version` to `NamedTypeRef` (positional, LAST, default null)
- **Acceptance.** `Gravity.Dsl.Ast/Types/NamedTypeRef.cs` declares `public sealed record NamedTypeRef(string Name, bool IsOptional, bool IsArray, SourceSpan Span, int? Version = null) : TypeRef(Span);` exactly as specified in FR-110 (revised after critic pass) and plan.md §3.3. `Version` is the **last** positional parameter with a default of `null`, which preserves the `1.0.0` 4-argument constructor signature: existing source code that did `new NamedTypeRef(name, isOpt, isArr, span)` continues to compile unchanged against the new record. `Gravity.Dsl.Tests/Helpers/SpanIgnoringEquality.cs` (the test-side equality helper at the actual repo path — NOT `Gravity.Dsl.Compiler/Equality/SpanIgnoringEquality.cs`, which does not exist) is updated: the `Equal(TypeRef, TypeRef)` helper's `NamedTypeRef` arm at line 150 gains a `na.Version == nb.Version` clause. No other AST record changes shape (FR-112).
- **Files.** `Gravity.Dsl.Ast/Types/NamedTypeRef.cs`, `Gravity.Dsl.Tests/Helpers/SpanIgnoringEquality.cs`.
- **Depends on.** T100.

### T102 [P]. Migrate every `new NamedTypeRef(...)` construction site
- **Acceptance.** Every `new NamedTypeRef(...)` site in the repo continues to compile because the new `Version` parameter has a default of `null` (T101); no positional rewrite is required at any of these sites. **Construction sites enumerated from `rg -n "new NamedTypeRef\(" /workspace/gravity/Gravity.Dsl.* /workspace/gravity/Gravity.Dsl.Tests/`**: a single production site at `Gravity.Dsl.Compiler/Parsing/Parser.cs:640` which T104 updates to pass `version` explicitly as the 5th positional argument. No other production or test construction sites exist (verified by grep). Build is green; existing Phase 0–3 tests still pass with no behaviour change (FR-112 stability promise). If new test fixture builders that construct `NamedTypeRef` in-memory are added by other Phase 8 tasks, they may either omit `Version` (defaults to `null`) or pass it explicitly — both are correct.
- **Files.** `Gravity.Dsl.Compiler/Parsing/Parser.cs:640` (sole production site; updated in T104).
- **Depends on.** T101.

### T103. Lexer: confirm `@` produces `TokenKind.At` and add coverage if missing
- **Acceptance.** `Gravity.Dsl.Compiler/Lexing/Lexer.cs` already emits `TokenKind.At` for `@` (Phase 0–3 added it for annotation prefixes; cf. T010 / FR-004). This task confirms (no behaviour change required) and adds one explicit lexer unit test if the existing suite does not already pin `@` → `TokenKind.At` in isolation. New test (if needed): `LexerTests.At_Produces_AtToken` asserting `Tokenize("@").Single().Kind == TokenKind.At`. If the suite already covers `@` via annotation-tokenization tests, this task is a no-op verified by re-reading `LexerTests.cs`; document the verification with a one-line comment in the new test or in the task's closing PR description.
- **Files.** `Gravity.Dsl.Tests/Lexing/LexerTests.cs` (additive only; only if not already covered).
- **Depends on.** T101.

### T104. Parser: extend `ParseNamedTypeRef` (or equivalent) to consume optional `@N`
- **Acceptance.** `Gravity.Dsl.Compiler/Parsing/Parser.cs` is extended at the insertion point named in plan.md §3.5 (immediately after the identifier read, before the `?`/`[]` modifier block). A new private helper `TryReadVersionSuffix(ParserState s, Token atTok, out int? version, out Diagnostic? diag)` performs the `@N` parse with `int.TryParse(lexeme, NumberStyles.None, CultureInfo.InvariantCulture, out int n)` and a column-adjacency check (`s.Peek().Span.Column == atTok.Span.Column + 1`) so `Foo@ 2` is rejected as malformed. On success `version = n` and the `NamedTypeRef` is constructed with `version` at positional index 1 (see T102). On failure the helper returns a `PARSE020` diagnostic with one of the message bodies enumerated in plan.md §3.5: "expected positive integer after '@'", "version suffix must not have a leading zero", "version suffix must be a positive integer", "whitespace is not allowed between '@' and the version number". Recovery follows FR-101: the parser continues with `version = null` so a single malformed suffix does not cascade. PARSE020 message bodies are stable; downstream tests grep on them.
- **Files.** `Gravity.Dsl.Compiler/Parsing/Parser.cs`.
- **Depends on.** T102, T107.

### T105. Parser: PARSE020 on `@N` after primitive types (FR-100)
- **Acceptance.** Per plan.md §3.5 (the "primitive-vs-named decision" branch at the existing line 624 site), when the parsed `TypeRef` resolves to a primitive (`PrimitiveTypeRef`) and a `@N` suffix was consumed, the parser emits `PARSE020` with body "version suffix is not permitted on primitive types" at the `@` token's span and discards the version. AC-8.12 negative case `properties { count: Int@2; }` produces exactly one `PARSE020`. Recovery: the parser keeps the primitive type and proceeds.
- **Files.** `Gravity.Dsl.Compiler/Parsing/Parser.cs`.
- **Depends on.** T104.

### T106. Parser: PARSE020 on `@N` on relation targets (FR-100)
- **Acceptance.** The relation parser site (plan.md §3.5 cites `Parser.cs:324` and `:410` as the relation-target entry points) refuses a `@N` suffix on relation targets with `PARSE020` body "version suffix is not permitted on relation targets". AC-8.12 negative case `relations { paid_by: Employee@2 cardinality one; }` produces exactly one `PARSE020`. Recovery: the relation parser skips the suffix and proceeds with the unqualified relation target so the rest of the relation block still parses. Note: relations remain Phase 0–3 single-version surfaces; the resolver will resolve the relation target to the maximum declared version (FR-126).
- **Files.** `Gravity.Dsl.Compiler/Parsing/Parser.cs`.
- **Depends on.** T104.

### T106a. Parser: PARSE020 on `@N` after `returns` in command declarations (FR-100)
- **Acceptance.** The command parser refuses a `@N` suffix in the `returns <Name>` slot with `PARSE020` body "version suffix is not permitted on command return types". `CommandDecl.ReturnsType` is a bare `string` in the v1 AST; the parser strips the `@N` and stores only the name when present. AC-8.12 negative case `commands { Submit() returns SubmissionResult@2 with side_effect Submitted; }` produces exactly one `PARSE020`. Recovery: the parser keeps the bare return-type identifier and proceeds. Note: lifting this restriction in Phase 9+ requires upgrading `CommandDecl.ReturnsType` from `string` to `TypeRef`, which is an AST shape change beyond Phase 8's additive-only envelope.
- **Files.** `Gravity.Dsl.Compiler/Parsing/Parser.cs`.
- **Depends on.** T104.

### T107. Add `PARSE020` constant to parser rule-id surface
- **Acceptance.** `Gravity.Dsl.Compiler/Parsing/RuleIds.cs` gains `public const string Parse020 = "PARSE020";` following the `Parse010` naming pattern locked by T065. The constant is referenced from every PARSE020 emission site (T104, T105, T106). No other rule-id constants change. This task is strictly textual — it lands before T104 in dependency order so the four PARSE020 call sites have a constant to reference.
- **Files.** `Gravity.Dsl.Compiler/Parsing/RuleIds.cs`.
- **Depends on.** —

### T108. SourceWriter: emit `@N` after the name, before `?`/`[]`
- **Acceptance.** `Gravity.Dsl.Compiler/Parsing/SourceWriter.cs::WriteTypeRef` is extended exactly as in plan.md §3.6: in the `NamedTypeRef` arm, after appending `n.Name` and before appending the type suffix, append `'@'` then `v.ToString(CultureInfo.InvariantCulture)` when `n.Version is { } v`. Order is locked: name → `@N` → `?` / `[]`. `int.ToString(CultureInfo.InvariantCulture)` is the determinism-safe form (the banned-APIs list permits `int.ToString(IFormatProvider)`). Every Phase 0–3 source-writer test still passes (a `NamedTypeRef` with `Version: null` writes exactly what it wrote before).
- **Files.** `Gravity.Dsl.Compiler/Parsing/SourceWriter.cs`.
- **Depends on.** T101.

### T109 [P]. Parser unit tests for AC-8.12 positive and negative cases
- **Acceptance.** `Gravity.Dsl.Tests/Parsing/ParserTests.cs` (or a new `VersionSuffixParserTests.cs` in the same folder) gains parameterised tests covering AC-8.12:
  - **Positive:** `properties { payment: Money@2; }` parses to `NamedTypeRef(Name: "Money", Version: 2, IsOptional: false, IsArray: false, ...)`. The modifier interactions `Money@2?`, `Money@2[]`, `Money@2?[]` each parse with `Version: 2` and the corresponding `IsOptional`/`IsArray` flags set.
  - **Negative (malformed `@N`, all PARSE020):** `Money@` (missing literal), `Money@01` (leading zero), `Money@-1` (negative), `Money@ 2` (whitespace gap, column-adjacency fails), `Money@+2` (leading `+`, `NumberStyles.None` rejects).
  - **Negative (illegal positions, PARSE020):** `properties { count: Int@2; }` (primitive), `relations { paid_by: Employee@2 cardinality one; }` (relation target), `commands { Submit() returns SubmissionResult@2 with side_effect Submitted; }` (command return type — FR-100 / T106a).
  Every negative assertion pins exact rule id (`PARSE020`) and a message substring from plan.md §3.5. Recovery is asserted: each negative fixture parses to a usable `SourceFile` with one PARSE020 in the diagnostic stream and the rest of the file structurally intact.
- **Files.** `Gravity.Dsl.Tests/Parsing/ParserTests.cs` or `Gravity.Dsl.Tests/Parsing/VersionSuffixParserTests.cs`.
- **Depends on.** T104, T105, T106.

### T110 [P]. Round-trip test fixtures for AC-8.16
- **Acceptance.** Two new fixtures under `tests/fixtures/parser/`:
  - `version_qualified_basic.gravity` — at least one value type plus an entity whose `properties` block carries fields of types `Money@2`, `Money@2?`, `Money@2[]`, `Money@2?[]`.
  - `version_qualified_in_command.gravity` — at least one command whose argument list and `returns` slot use `@N`-qualified type refs.
  The existing `Gravity.Dsl.Tests/Parsing/RoundTripTests.cs` harness (locked by T022) picks both up automatically because it scans `tests/fixtures/parser/*.gravity`; no harness change is required. Each fixture goes parse → write → parse and asserts AST structural equality under `SpanIgnoringEquality.Equal`, which now compares `NamedTypeRef.Version` (see T101). AC-8.16 closure: a source containing `Money@2` round-trips with `NamedTypeRef.Version == 2` preserved.
- **Files.** `tests/fixtures/parser/version_qualified_basic.gravity`, `tests/fixtures/parser/version_qualified_in_command.gravity`.
- **Depends on.** T104, T108.

### T111 [P]. AST stability regression test (AC-8.14)
- **Acceptance.** A new `Gravity.Dsl.Tests/AstStabilityTests.cs` (per plan.md §5.5) loads the vendored Phase 0–3 AST assembly from `tests/vendor/Gravity.Dsl.Ast.1.0.0/` into an isolated `AssemblyLoadContext` and asserts that a stub `IEmitter` compiled against `1.0.0` is loaded successfully by the live emitter host (`EmitterRegistry` plus the version-range admission check from T032 / `HOST001`). The same test asserts `AstVersion.Value == "1.1.0"` literally. Phase 0–3 third-party emitters built against `1.0.0` continue to function — this is the FR-111 / LD-8 promise made enforceable. The vendor directory is populated as a one-time setup step in the same PR (sourced from the `Phase 3` published `Gravity.Dsl.Ast` 1.0.0 nupkg under `nupkgs/`).
- **Files.** `Gravity.Dsl.Tests/AstStabilityTests.cs`, `tests/vendor/Gravity.Dsl.Ast.1.0.0/Gravity.Dsl.Ast.dll` (vendored binary).
- **Depends on.** T100.

---

## Sub-phase P8b — Resolver multi-version (T120–T129)

Goal: re-key the resolved-model declaration map by `(FQN, Version)`, admit coexisting versions only through an explicit `deprecates` chain, and add the missing-version variant of `RES003`. Closes FR-120, FR-121, FR-122, FR-123, FR-126, FR-127, FR-161. Closes AC-8.7, AC-8.8 (`RES004` half), AC-8.13. No validator-layer breaking-change rules run yet — those land under P8c.

### T120. Introduce `DeclKey` and its comparer
- **Acceptance.** New file `Gravity.Dsl.Compiler/Versioning/DeclKey.cs` declares the `internal readonly record struct DeclKey(string Fqn, int Version) : IComparable<DeclKey>` exactly as in plan.md §3.1 (FQN compared via `string.CompareOrdinal`, then `Version` ascending). Sibling `internal sealed class DeclKeyComparer : IComparer<DeclKey>` exposes a singleton `Instance`. The file is `internal` to `Gravity.Dsl.Compiler` (no AST-package leak). Unit test `DeclKeyTests` covers equality, comparison ordering, and `ImmutableSortedDictionary` integration (a builder constructed with `DeclKeyComparer.Instance` iterates in `(Fqn ordinal, Version asc)` order across a hand-rolled 4-entry set).
- **Files.** `Gravity.Dsl.Compiler/Versioning/DeclKey.cs`, `Gravity.Dsl.Tests/Versioning/DeclKeyTests.cs`.
- **Depends on.** —

### T121. Re-key `ResolvedModel.Declarations` from `string` to `DeclKey`
- **Acceptance.** `Gravity.Dsl.Compiler/Resolution/ResolvedModel.cs::Declarations` changes type from `ImmutableSortedDictionary<string, TopLevelDecl>` to `ImmutableSortedDictionary<DeclKey, TopLevelDecl>` exactly as in plan.md §3.2. Iteration order is `(Fqn ordinal asc, Version asc)` — the FR-161 contract. `FileImports` retains its `string`-keyed inner map (per-file simple-name scope continues to resolve to a single canonical decl per simple name). Every `model.Declarations` consumer listed in plan.md §3.2 is migrated:
  - `Gravity.Dsl.Compiler/Validation/Validator.cs` — `foreach (var kv in model.Declarations)` is unchanged (only reads `kv.Value`).
  - `Gravity.Dsl.Emitter.CSharp/CSharpEmitter.cs` — same.
  - `Gravity.Dsl.Tests/Stubs/NoopEmitter.cs` — same.
  - `Gravity.Dsl.Compiler/Resolution/Resolver.cs` — builder construction site changes to `ImmutableSortedDictionary.CreateBuilder<DeclKey, TopLevelDecl>(DeclKeyComparer.Instance)`.
  Build is green; **every** Phase 0–3 test still passes (the iteration shape is unchanged when each FQN has a single version, so emitter output is byte-identical against the locked goldens from T048).
- **Files.** `Gravity.Dsl.Compiler/Resolution/ResolvedModel.cs`, `Gravity.Dsl.Compiler/Resolution/Resolver.cs`, `Gravity.Dsl.Compiler/Validation/Validator.cs`, `Gravity.Dsl.Emitter.CSharp/CSharpEmitter.cs`, `Gravity.Dsl.Tests/Stubs/NoopEmitter.cs`.
- **Depends on.** T120.

### T122. Resolver: build version index per FQN and admit chained coexistence (FR-122)
- **Acceptance.** A new private helper `BuildVersionIndex(declMap)` in `Resolver.cs` returns `ImmutableSortedDictionary<string, ImmutableArray<int>>` mapping each FQN to its declared versions in ascending order. The resolver then iterates each FQN group with `versions.Length > 1` and for every adjacent pair `(prev, next)` checks the chain admission condition documented in plan.md §3.4(c): the higher-versioned declaration must be an `EntityDecl` whose `Deprecates?.Version == prev`. When the chain holds, no diagnostic fires and both decls remain in `Declarations`. When the chain does not hold at a given link, `RES004` fires at the higher-versioned decl's span (see T124). `ValueTypeDecl` and `EnumDecl` carry no `DeprecatesClause` in the v1 grammar, so a second version of either always trips `RES004`. The new helper sits in `Resolver.cs` (not in `Versioning/`) because it depends on internal `declMap` shape; it is `private static`.
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`.
- **Depends on.** T121.

### T123. Resolver: skipped-link detection helper (sets up VAL027 input)
- **Acceptance.** Per plan.md §3.4(c), a skipped chain link (e.g. `v3 deprecates v1` while `v2` coexists) surfaces in the resolver as `RES004` on the adjacency where the chain check fails. The same condition is also reported as `VAL027` in the validator pass (T150) — the resolver does not need to emit `VAL027` itself, but it does need to expose the version index so the validator can reconstruct the broken-link information without re-walking the AST. This task adds `VersionIndex` to `ResolvedModel` as an **`init`-only property** (NOT a new positional record argument, to keep the record's primary constructor arity stable): `public ImmutableDictionary<string, ImmutableArray<int>> VersionIndex { get; init; } = ImmutableDictionary<string, ImmutableArray<int>>.Empty;`. The resolver constructs `ResolvedModel` via the existing primary ctor and sets `VersionIndex` via object-initializer at the construction site (e.g. `new ResolvedModel(declMap, files, fileImports) { VersionIndex = BuildVersionIndex(declMap) }`). The accessor is documented as "FQN → versions ascending; populated by the resolver, consumed by the validator's breaking-change pass". The resolver's emission of `RES004` at the gap is exercised in the same fixture that T128's AC-8.8 first half pins.
- **Files.** `Gravity.Dsl.Compiler/Resolution/ResolvedModel.cs`, `Gravity.Dsl.Compiler/Resolution/Resolver.cs`.
- **Depends on.** T122.

### T124. Resolver: re-fire `RES004` only on unchained collisions (FR-123)
- **Acceptance.** `RES004`'s emission site moves from "any two decls share an FQN" to "two decls share an FQN and the chain admission of T122 failed at this link". The rule id is preserved (Phase 0–3 tooling that filters on `RES004` continues to work). The message body is updated to: `"entity '<FQN>' is declared more than once; multi-version coexistence requires a deprecates chain"`. Phase 0–3 fixtures that intentionally exercise duplicate-version FQNs (T024's `version disambiguation when two versions of an entity exist`) are updated to either (a) add a `deprecates` clause and assert no `RES004`, or (b) keep the unchained case and assert the new RES004 message body. The Phase 0–3 `RES004` round-trip behaviour is preserved for the single-version duplicate-FQN case (FR-121): two decls at the **same** `(FQN, Version)` still trip `RES004` with no message-body change in that branch.
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`, `Gravity.Dsl.Tests/Resolution/ResolverTests.cs` (existing tests updated to match new message body where applicable).
- **Depends on.** T122.

### T124a. RES004 message-body migration grep (audit)
- **Acceptance.** Atomic with T124's message-body change: `rg "Phase 0–3 disallows multiple in-scope versions"` returns **zero** hits across the repository after this task. The old Phase 0–3 message body (currently at `Gravity.Dsl.Compiler/Resolution/Resolver.cs:56`) is migrated to the new body locked in T124 ("multi-version coexistence requires a deprecates chain"). Every Phase 0–3 test or golden file that asserts on the old wording is updated to assert on the new wording. The grep covers at least: `Gravity.Dsl.Tests/Resolution/ResolverTests.cs`, every `.txt` / `.golden` under `tests/golden/diagnostics/` (if present), and any spec/plan/tasks markdown files that quote the old body verbatim. Migration is performed in the same commit as T124 so the test suite remains green throughout.
- **Files.** `Gravity.Dsl.Tests/Resolution/ResolverTests.cs` (and any other hit from the grep).
- **Depends on.** T124.

### T125. Resolver: unqualified `NamedTypeRef` resolves to max version in imports-transitive scope (FR-126)
- **Acceptance.** `Resolver.CheckTypeRef` (or its equivalent type-ref checking site) becomes version-aware. When `named.Version is null`, the resolver invokes `BindUnqualified(typeName, fpath)` which consults `VersionIndex[fqn]` **filtered to versions whose declaring file is `fpath` itself or transitively imported by `fpath`** — this pins the FR-126 "scope means imports-transitive" interpretation (not "max in model"). The resolver resolves the type ref to the maximum of the filtered set. AC-8.13b's cross-file fixture exercises this filter: file A imports only file B (declaring v1), file C declares v2 but is not imported; an unqualified ref in file A binds to v1, not v2. The resolver's internal lookup table records the bound `(FQN, Version)` pair, but per FR-126 the AST node itself is **not** mutated (the source-written form must be preserved for round-trip; see AC-8.16). When the simple name is not in scope, the existing Phase 0–3 `RES003` "name '<X>' is not defined or imported in this scope" message fires with no text change.
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`.
- **Depends on.** T123.

### T126. Resolver: `NamedTypeRef.Version = N` resolves exactly; missing-version variant of `RES003` (FR-127)
- **Acceptance.** When `named.Version is { } v`, the resolver looks up the declaration at `(fqn, v)` exactly. On hit, the type-ref is bound to that exact decl. On miss with the simple name **in scope** under at least one other version, the resolver emits the missing-version variant of `RES003`: `"type '<Name>@<v>' is not declared; '<Name>' exists with versions <list>"` where `<list>` is rendered as `1, 2` (comma-space, ascending, ordinal). On miss with the simple name **not in scope** at all, the resolver emits the unchanged Phase 0–3 `RES003` body `"name '<Name>' is not defined or imported in this scope"`. The two messages are textually distinct (constitution Quality-standards bar on missing-import-vs-missing-definition distinction, extended to missing-version). Existing `RES003` callers that grep on the old text continue to function for the non-versioned path.
- **Files.** `Gravity.Dsl.Compiler/Resolution/Resolver.cs`.
- **Depends on.** T123.

### T127 [P]. Resolver tests for AC-8.7 (chained coexistence)
- **Acceptance.** New fixtures under `tests/fixtures/versioning/resolver/`:
  - `chain_ok_two_versions.gravity` — `Employee version 1 { ... }` plus `Employee version 2 deprecates version 1 until "2099-12-31" { ... }`. Resolver emits zero diagnostics; `Declarations` contains both `DeclKey("...Employee", 1)` and `DeclKey("...Employee", 2)`.
  - `chain_ok_three_versions.gravity` — `Employee@1`, `Employee@2 deprecates 1`, `Employee@3 deprecates 2`. Resolver emits zero diagnostics; the version index for the FQN is `[1, 2, 3]`.
  The test in `Gravity.Dsl.Tests/Resolution/ResolverTests.cs` (or a new `MultiVersionResolverTests.cs` next to it) asserts diagnostic count, the presence of every `DeclKey`, and — for `chain_ok_three_versions` — that the version index is exactly `[1, 2, 3]`. AC-8.7's "breaking-change pass runs twice" assertion is not made here (no validator yet); it is made under T156..T161 once VAL020..VAL026 land.
- **Files.** `tests/fixtures/versioning/resolver/chain_ok_two_versions.gravity`, `tests/fixtures/versioning/resolver/chain_ok_three_versions.gravity`, `Gravity.Dsl.Tests/Resolution/MultiVersionResolverTests.cs`.
- **Depends on.** T124.

### T128 [P]. Resolver tests for AC-8.8 (broken chain — both no-chain and skipped-link)
- **Acceptance.** New fixtures under `tests/fixtures/versioning/resolver/`:
  - `chain_missing.gravity` — `Employee@1` plus `Employee@2` (no `deprecates`). The resolver emits exactly one `RES004` at `Employee@2.Span` with the message body locked in T124 ("multi-version coexistence requires a deprecates chain"). No breaking-change diagnostics fire (no validator yet; AC-8.8 first half).
  - `chain_skipped_link.gravity` — `Employee@1`, `Employee@2` (no `deprecates`), `Employee@3 deprecates version 2`. The resolver emits exactly one `RES004` at the `(1, 2)` gap (i.e. on `Employee@2.Span`). The `(2, 3)` link is chained and emits nothing. The test additionally asserts the version index is `[1, 2, 3]` so T150 (`VAL027`) can pin its half of AC-8.8 against the same fixture.
- **Files.** `tests/fixtures/versioning/resolver/chain_missing.gravity`, `tests/fixtures/versioning/resolver/chain_skipped_link.gravity`, `Gravity.Dsl.Tests/Resolution/MultiVersionResolverTests.cs`.
- **Depends on.** T124.

### T129 [P]. Resolver tests for AC-8.13 (unqualified → max, qualified → exact, missing-version variant of `RES003`)
- **Acceptance.** Three fixtures under `tests/fixtures/versioning/resolver/`:
  - `unqualified_resolves_to_max.gravity` — `Project@1`, `Project@2 deprecates 1`, plus an entity with property `lead_project: Project;`. The test reads the resolver's binding via the new **internal `Resolver.ResolveWithBindings(files, inputRoot)` overload** (plan.md §3.4(e)) which returns `(ResolveResult result, IReadOnlyDictionary<NamedTypeRef, DeclKey> bindings)`. `[InternalsVisibleTo("Gravity.Dsl.Tests")]` is added to `Gravity.Dsl.Compiler/AssemblyInfo.cs` if not already present. The test asserts the property's `NamedTypeRef` binds to `DeclKey("...Project", 2)`. This is the **only** test-hook mechanism chosen — alternatives (public property on `ResolvedModel`, separate accessor) are rejected to keep the compiler library's public surface stable.
  - `qualified_resolves_exact.gravity` — same FQN setup plus property `lead_project_legacy: Project@1;`. The binding asserts `DeclKey("...Project", 1)`.
  - `qualified_missing_version.gravity` — `Project@1`, `Project@2 deprecates 1`, plus property `lead_project: Project@5;`. The resolver emits exactly one `RES003` whose message contains both `'Project@5'` and the list rendering `1, 2` (locked in T126).
  - **Cross-file fixture (AC-8.13b)**: `cross_file_imports/a.gravity` imports only `cross_file_imports/defs_v1.gravity` (declaring `Project version 1`); `cross_file_imports/defs_v2.gravity` declares `Project version 2 deprecates version 1 until "2099-12-31"` but is **not** imported by `a.gravity`. An unqualified `Project` ref inside `a.gravity` binds to `DeclKey("...Project", 1)` — the import-filtered max. This pins FR-126's imports-transitive semantics.
- **Files.** `tests/fixtures/versioning/resolver/unqualified_resolves_to_max.gravity`, `tests/fixtures/versioning/resolver/qualified_resolves_exact.gravity`, `tests/fixtures/versioning/resolver/qualified_missing_version.gravity`, `tests/fixtures/versioning/resolver/cross_file_imports/{a,defs_v1,defs_v2}.gravity`, `Gravity.Dsl.Tests/Resolution/MultiVersionResolverTests.cs`, `Gravity.Dsl.Compiler/Resolution/Resolver.cs` (the `ResolveWithBindings` overload), `Gravity.Dsl.Compiler/AssemblyInfo.cs` (`InternalsVisibleTo` if not already present).
- **Depends on.** T125, T126.

---

## Sub-phase P8c — Validator diff engine + CLI (T140–T169)

Goal: implement the eight breaking-change rules of the validator's new diff pass, thread `DateOnly currentDate` through the validator and the CLI, and add the `--as-of YYYY-MM-DD` flag. Closes FR-124, FR-125, FR-130, FR-131, FR-132, FR-133, FR-134, FR-135, FR-136, FR-137, FR-138, FR-140, FR-141, FR-142, FR-150, FR-151 (`VAL020..VAL030`), FR-160. Closes AC-8.1, AC-8.2, AC-8.3, AC-8.4, AC-8.5, AC-8.6, AC-8.8 (`VAL027` half), AC-8.9, AC-8.10, AC-8.11, AC-8.15.

### T140. Add eleven `VAL020..VAL030` constants to validator rule-id surface
- **Acceptance.** `Gravity.Dsl.Compiler/Validation/RuleIds.cs` gains eleven new `public const string` entries, one per rule, exactly as in FR-151 and plan.md §3.8: `Val020 = "VAL020"`, `Val021 = "VAL021"`, `Val022 = "VAL022"`, `Val023 = "VAL023"`, `Val024 = "VAL024"`, `Val025 = "VAL025"`, `Val026 = "VAL026"`, `Val027 = "VAL027"`, `Val028 = "VAL028"`, `Val029 = "VAL029"`, `Val030 = "VAL030"`. Naming follows the existing pattern locked by T025 (`Val001..Val010`). No other rule-id constants change. The task lands first within P8c so every subsequent diff-rule implementation has a constant to reference.
- **Files.** `Gravity.Dsl.Compiler/Validation/RuleIds.cs`.
- **Depends on.** —

### T141 [P]. `Narrowing.cs` — type-narrowing predicate (FR-131)
- **Acceptance.** New file `Gravity.Dsl.Compiler/Versioning/Narrowing.cs` declares `internal static class Narrowing` with `public static bool IsNarrowing(TypeRef prev, TypeRef next)` implementing the table from FR-131 exactly as in plan.md §3.9:
  - Optionality lost (`T?` → `T`) narrows.
  - Array-ness lost (`T[]` → `T`) narrows.
  - Primitive narrowings: `Decimal→Int`, `Long→Int`, `String→<any non-String primitive>`, `DateTime→Date`.
  - Primitive widenings (NOT narrowing): `Int→Long`, `Int→Decimal`, `Long→Decimal`, `Date→DateTime`.
  - Named-named: same name with `Version` decreasing narrows (e.g. `Money@2` → `Money@1`).
  - Named-named: different `Name` is a rename, returns `false` (the rename is reported as VAL020 add+remove, not VAL021; locked by FR-131 last bullet and the rule body comment in plan.md §3.8).
  - Cross-kind (primitive ↔ named): treated as narrowing (FR-131 implicit; plan.md §3.9 "the safest assumption per Principle IV is 'this is a contract change'").
  Unit tests `NarrowingTests` parameterise over every row of the table (nine narrowing rows, four widening rows, the rename suppression case, the cross-kind case) and assert the boolean result. The table itself is locked by spec — any future change requires a spec amendment.
- **Files.** `Gravity.Dsl.Compiler/Versioning/Narrowing.cs`, `Gravity.Dsl.Tests/Versioning/NarrowingTests.cs`.
- **Depends on.** T120.

### T142. `VersionDiff.cs` — per-pair walker + `DiagnosticSink`
- **Acceptance.** New file `Gravity.Dsl.Compiler/Versioning/VersionDiff.cs` implements the walker described in plan.md §3.8: group `model.Declarations` by `DeclKey.Fqn`, walk adjacent `(prev, next)` pairs that are chained (the resolver already rejected unchained pairs via T124's `RES004`), invoke every rule in `DiffRules.All` for each chained pair, then call the per-decl passes (`DiffRules.ApplyAcrossModel(model, sink)` for `VAL027`/`VAL028`/`VAL029`) and the window check (`DiffRules.ApplyWindow(model, currentDate, sink)` for `VAL030`). The companion `DiagnosticSink` accumulates diagnostics with `(Fqn, Vnext, RuleId, Span)` keys and produces a stably-sorted output via the FR-160 ordering (see T155). `VersionDiff.Run(ResolvedModel model, DateOnly currentDate)` is the single public entry point and returns `IReadOnlyList<Diagnostic>`. The skeleton lands here; per-rule bodies land under T143..T153.
- **Files.** `Gravity.Dsl.Compiler/Versioning/VersionDiff.cs`, `Gravity.Dsl.Compiler/Versioning/DiagnosticSink.cs`.
- **Depends on.** T121, T140.

### T143. VAL020 — field removal (FR-130)
- **Acceptance.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs::ApplyVal020(prev, next, sink)` (or sibling file) is implemented per the reference body in plan.md §3.8 for all four containers:
  - **`entity-property`** — names in `prev.Properties` absent from `next.Properties`.
  - **`value-type-field`** — names in `prev.Fields` absent from `next.Fields` (for `ValueTypeDecl` pairs).
  - **`event-payload`** — for each event surviving from `prev` to `next` (`prevEvt.Name == nextEvt.Name`), names in `prevEvt.Payload` absent from `nextEvt.Payload`.
  - **`command-argument`** is **not** reported under VAL020; it is reported under VAL026 (FR-136). The VAL020 implementation must explicitly skip the command arg container so as not to double-count with T149.
  Message body matches FR-130: `"<container>.<name> was removed in <FQN>@<Vnext>; field removal is a breaking change"`. Diagnostic severity is `Error`. Diagnostic order follows the `prev` declaration order (the helper `DiffByName` streams `prev.Except(next, StringComparer.Ordinal)` in `prev` order, which is deterministic and matches FR-160 secondary keys). Hint text per FR-150 (the one-clause hint about `?` / `deprecates` / future `@deprecated` annotation) is appended to the entity-property variant only.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T144. VAL021 — type narrowing on surviving fields (FR-131)
- **Acceptance.** `DiffRules.ApplyVal021(prev, next, sink)` iterates every same-named field surviving from `prev` to `next` (entity properties, value-type fields, event payload fields) and emits `VAL021` when `Narrowing.IsNarrowing(prevField.Type, nextField.Type)` returns true. The message body matches FR-131: `"<container>.<name>: type narrowed from <prev> to <next> in <FQN>@<Vnext>"` where the `<prev>` / `<next>` forms render the full surface (`String?[]`, `Money@2`, etc.) so the fix is obvious. Renderer is shared with the source-writer's `WriteTypeRef` (T108) so optional/array modifiers and `@N` suffixes appear in the diagnostic exactly as in source. Rename suppression: when `prevField.Type` and `nextField.Type` are both `NamedTypeRef` with different `Name`, `IsNarrowing` returns false (T141), so VAL021 does not fire on renames; the rename is reported under VAL020 via T143's add+remove path. Diagnostic order: same as T143.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`, `Gravity.Dsl.Compiler/Versioning/TypeRefRenderer.cs` (shared renderer; may already live in `SourceWriter` — if so, factor out a static helper and reference both sites).
- **Depends on.** T141, T142.

### T145. VAL022 — lifecycle state removed (FR-132)
- **Acceptance.** `DiffRules.ApplyVal022(prev, next, sink)` iterates `prev.Lifecycle.States` (when `prev` is an `EntityDecl`) and emits `VAL022` for every state name absent from `next.Lifecycle.States`. Message body: `"lifecycle state '<state>' removed from <FQN>@<Vnext>; state removal is a breaking change"`. Severity `Error`. Adding states fires nothing. Diagnostic order follows `prev.Lifecycle.States` declaration order.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T146. VAL023 — command removed (FR-133)
- **Acceptance.** `DiffRules.ApplyVal023(prev, next, sink)` iterates `prev.Commands` (when `prev` is an `EntityDecl`) and emits `VAL023` for every command name absent from `next.Commands`. Message body: `"command '<name>' removed from <FQN>@<Vnext>; command removal is a breaking change"`. Severity `Error`. AC-8.5 rename case: renaming a command is reported as `VAL023` on the removal; the addition is not flagged unless it introduces a required-argument issue under VAL026 (T149).
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T147. VAL024 — event removed (FR-134)
- **Acceptance.** `DiffRules.ApplyVal024(prev, next, sink)` iterates `prev.Events` (when `prev` is an `EntityDecl`) and emits `VAL024` for every event name absent from `next.Events`. Message body: `"event '<name>' removed from <FQN>@<Vnext>; event removal is a breaking change"`. Severity `Error`. Cross-rule interaction: a command's `with side_effect <event>` change is detected here when the new event does not exist (see FR-136 last sentence); the command-argument rule (T149) does not double-count.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T148. VAL025 — transition removed (warning, FR-135)
- **Acceptance.** `DiffRules.ApplyVal025(prev, next, sink)` iterates `prev.Lifecycle.Transitions` (when `prev` is an `EntityDecl`) and emits `VAL025` for every `(From, To, OnEvent)` triple absent from `next.Lifecycle.Transitions`. Message body: `"transition '<From> -> <To> on <OnEvent>' removed from <FQN>@<Vnext>"`. **Severity `Warning`** — explicitly not `Error` (FR-135 calls out the soft-warning rationale). AC-8.4 fixture asserts severity is `Warning`. The rule's hint text per FR-150 references the future-promotion-to-error contingency once runtime guards land in Phase 9+.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T149. VAL026 — command argument breaking change (FR-136, four sub-causes)
- **Acceptance.** `DiffRules.ApplyVal026(prev, next, sink)` iterates commands surviving from `prev` to `next` (`prevCmd.Name == nextCmd.Name`) and runs four sub-checks, each emitting `VAL026` with a sub-cause appended to the message:
  - **(a) argument removed** — any `arg.Name` in `prevCmd.Args` missing from `nextCmd.Args`. Sub-cause text: `"argument removed"`.
  - **(b) argument renamed** — detected as (a) on the removal side; the addition is checked under (d). VAL026 fires once per removed name with sub-cause `"argument removed"`.
  - **(c) argument type narrowed** — for every same-named arg, run `Narrowing.IsNarrowing(prevArg.Type, nextArg.Type)`. Sub-cause text: `"argument type narrowed from <prev> to <next>"` using the shared renderer from T144.
  - **(d) new required argument added** — any `arg.Name` in `nextCmd.Args` not present in `prevCmd.Args` whose type is **not** optional (`IsOptional == false`). Sub-cause text: `"required argument added"`.
  Severity `Error`. Optional argument added produces zero diagnostics (locked by AC-8.6 last sentence). **Return type narrowing** is reported under VAL021 treating the `ReturnsType` slot as a single field named `<return>` (FR-136 last sentence; the `ApplyVal021` path in T144 must include this slot). A `with side_effect <event>` change is reported under VAL024 (T147) when the new event does not exist.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T141, T142, T144.

### T150. VAL027 — deprecates chain broken (FR-137)
- **Acceptance.** `DiffRules.ApplyVal027(model, sink)` (per-decl across the whole model — not per-pair) iterates each FQN's version index from `model.VersionIndex` (exposed by T123). For every FQN with three or more versions, every "intermediate" version `Vmid` must be either (a) named by `Vmid+1`'s `deprecates` clause, or (b) absent from the in-scope declaration set. A coexisting `Vmid` not referenced by `Vmid+1`'s deprecates clause emits `VAL027` at `Vmid+1.Span` with body `"deprecates chain broken: <FQN>@<Vmid> coexists with <FQN>@<Vmid+1> but is not named in its deprecates clause"`. This complements `RES004` (T124) which catches the no-chain-at-all case at the same gap; AC-8.8 second half asserts that the skipped-link fixture produces exactly one `VAL027` in addition to the resolver's `RES004` at the lower gap.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T123, T142.

### T151. VAL028 — deprecates names non-existent version (FR-124)
- **Acceptance.** `DiffRules.ApplyVal028(model, sink)` iterates every `EntityDecl` whose `Deprecates` is non-null. For each such decl, if `Deprecates.Version` does not appear in `model.VersionIndex[fqn]`, emit `VAL028` at `decl.Deprecates.Span` (or `decl.Span` if the clause carries no span) with body `"deprecates version <N> references no declared version of <FQN>"`. Severity `Error`. This is a validator rule, not a resolver rule, because the resolver's per-file pass may not yet have the merged version index when individual files are checked (FR-124 rationale). AC-8.9 fixture pins one `VAL028` and zero `VAL027` on a model where `Employee@2 deprecates version 9` and no `Employee@9` exists.
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T123, T142.

### T152. VAL029 — deprecates self / forward reference (FR-125)
- **Acceptance.** `DiffRules.ApplyVal029(model, sink)` iterates every `EntityDecl` whose `Deprecates` is non-null. For each such decl, if `Deprecates.Version >= decl.Version`, emit `VAL029` at `decl.Deprecates.Span` with body `"entity <FQN>@<self> may not deprecate version <N>; <N> must be strictly less than <self>"`. Severity `Error`. Both self-deprecation (`Employee@2 deprecates 2`) and forward-deprecation (`Employee@2 deprecates 3`) trip this rule. AC-8.9 fixture pins exactly one `VAL029` per faulty decl and zero `VAL027` (the chain-break check correctly excludes self/forward cases from its loop because `VersionIndex[fqn]` only contains declared versions).
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T153. VAL030 — deprecation window expired (FR-138, takes `DateOnly currentDate`)
- **Acceptance.** `DiffRules.ApplyVal030(model, currentDate, sink)` iterates every `EntityDecl` whose `Deprecates` is non-null. For each such decl, parse `Deprecates.UntilIso8601` to a `DateOnly` using `DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)` (well-formedness already enforced by `VAL009` in Phase 0–3 so the parse is safe). If `until < currentDate` (strict less-than per FR-138), emit `VAL030` at `decl.Deprecates.Span` with body `"deprecation window for <FQN>@<Vprev> expired on <date>; remove the deprecated version or extend the window"` where `<Vprev>` is `Deprecates.Version`. Severity `Error`. **Strict less-than** is asserted in the AC-8.10 fixture: `currentDate == until` produces zero diagnostics. `<date>` is rendered via `until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` (determinism-safe).
- **Files.** `Gravity.Dsl.Compiler/Versioning/DiffRules.cs`.
- **Depends on.** T142.

### T154. `Validator.Validate` signature gains `DateOnly currentDate`; thread to all callers (FR-140)
- **Acceptance.** `Gravity.Dsl.Compiler/Validation/Validator.cs::Validate` gains a third required parameter `DateOnly currentDate` (no default), matching the spec FR-140 signature `Validate(ResolvedModel model, IReadOnlyCollection<string> claimedAnnotationNamespaces, DateOnly currentDate)`. The validator's existing per-decl rules (VAL001..VAL010) run unchanged. The new diff pass `VersionDiff.Run(model, currentDate)` runs after the per-decl rules and its results are concatenated with the existing diagnostic list, with one final sort applied (see T155). Every caller is migrated per plan.md §3.7:
  - `Gravity.Dsl.Cli/CompilerPipeline.cs::RunCheck` — passes `currentDate` threaded from `--as-of` (T168).
  - `Gravity.Dsl.Cli/CompilerPipeline.cs::RunGen` — same.
  - `Gravity.Dsl.Tests/Validation/ValidatorTests.cs` and every other test file calling `Validate(...)` — add `default(DateOnly)` (== `0001-01-01`) for Phase 0–3 tests; tests exercising VAL030 pass a deterministic value (`2026-05-18` or per-fixture).
  Build is green. Phase 0–3 validator tests pass unchanged (no VAL030 ever fires at `0001-01-01`).
- **Files.** `Gravity.Dsl.Compiler/Validation/Validator.cs`, `Gravity.Dsl.Cli/CompilerPipeline.cs`, every test file under `Gravity.Dsl.Tests/**` that calls `Validator.Validate(...)` (mechanical migration).
- **Depends on.** T143, T144, T145, T146, T147, T148, T149, T150, T151, T152, T153.

### T155. Diagnostic ordering: implement the FR-160 final-sort step on Phase 8 diagnostics
- **Acceptance.** `Validator.Validate`'s output for the Phase 8 block (the diagnostics produced by `VersionDiff.Run`) is sorted by the FR-160 keys: (1) FQN ordinal ascending, (2) `Vnext` ascending (for per-decl rules, the decl's own `Version` is used as the secondary key per plan.md §3.8), (3) rule id ordinal ascending, (4) span path ordinal ascending, (5) span line ascending, (6) span column ascending. The Phase 0–3 block (VAL001..VAL010) flows through unchanged and is concatenated **after** the Phase 8 block so existing golden files (T026) for those rules remain byte-identical. The combined diagnostic list returned to callers is `[phase8-sorted..., phase0to3-as-before...]` — order is fixed and pinned by AC-8.15's combined-fixture golden file (T164).
- **Files.** `Gravity.Dsl.Compiler/Validation/Validator.cs`, `Gravity.Dsl.Compiler/Versioning/DiagnosticSink.cs`.
- **Depends on.** T142, T154.

### T156 [P]. Validator tests AC-8.1 (VAL020 — field removal)
- **Acceptance.** New fixture `tests/fixtures/versioning/validator/val020_field_removed.gravity` declares `Employee version 1 { properties { hireDate: Date; manager_id: UUID; } }` and `Employee version 2 deprecates version 1 until "2099-01-01" { properties { hireDate: Date; } }`. Test asserts exactly one `VAL020` diagnostic naming `manager_id`. A second fixture `tests/fixtures/versioning/validator/val020_no_chain.gravity` declares the same two versions but drops the `deprecates` clause; test asserts `RES004` is present and **zero** `VAL020` (the breaking-change pass does not run without a chain — closes the second sentence of AC-8.1). A third fixture `val020_value_type_field.gravity` exercises the value-type-field container variant. A fourth fixture `val020_event_payload.gravity` exercises the event-payload variant. Test framework in `Gravity.Dsl.Tests/Validation/Val020Tests.cs`.
- **Files.** `tests/fixtures/versioning/validator/val020_field_removed.gravity`, `val020_no_chain.gravity`, `val020_value_type_field.gravity`, `val020_event_payload.gravity`, `Gravity.Dsl.Tests/Validation/Val020Tests.cs`.
- **Depends on.** T154.

### T157 [P]. Validator tests AC-8.2 (VAL021 — nine narrowing rows)
- **Acceptance.** Nine fixtures under `tests/fixtures/versioning/validator/`, one per row of FR-131:
  - `val021_optional_lost.gravity` — `T?` → `T`.
  - `val021_array_lost.gravity` — `T[]` → `T`.
  - `val021_decimal_to_int.gravity`, `val021_long_to_int.gravity`.
  - `val021_string_to_int.gravity`, `val021_string_to_uuid.gravity` (representing the `String → <any non-String primitive>` row).
  - `val021_datetime_to_date.gravity`.
  - `val021_version_decrease.gravity` — `Money@2` → `Money@1` on a surviving field; pins the named-named version-decrease rule.
  - `val021_rename_no_val021.gravity` — same-position field with a different `Name`; asserts **zero** `VAL021` (the rename surfaces as `VAL020` add+remove instead). This is the rename-suppression contract from FR-131 last bullet.
  Each test asserts exactly one `VAL021` (or zero for the rename case) and that the rendered `<prev>` / `<next>` forms in the message match the source surface (e.g. `String?[]`, `Money@2`). Test framework in `Gravity.Dsl.Tests/Validation/Val021Tests.cs`.
- **Files.** Nine `tests/fixtures/versioning/validator/val021_*.gravity` files, `Gravity.Dsl.Tests/Validation/Val021Tests.cs`.
- **Depends on.** T154.

### T158 [P]. Validator tests AC-8.3 (widening allowed)
- **Acceptance.** Four fixtures, one per widening row in FR-131:
  - `val021_widen_int_to_long.gravity` — `Int` → `Long`.
  - `val021_widen_int_to_decimal.gravity` — `Int` → `Decimal`.
  - `val021_widen_long_to_decimal.gravity` — `Long` → `Decimal`.
  - `val021_widen_date_to_datetime.gravity` — `Date` → `DateTime`.
  Each fixture produces zero `VAL021` diagnostics. A fifth fixture `val021_reordered_no_diagnostic.gravity` re-orders properties without changing names or types and asserts zero `VAL021` (pins plan.md §6 risk-register row on name-keyed vs index-keyed diff). Test framework in `Gravity.Dsl.Tests/Validation/Val021WideningTests.cs`.
- **Files.** Five `tests/fixtures/versioning/validator/val021_widen_*.gravity` / `val021_reordered_*.gravity` files, `Gravity.Dsl.Tests/Validation/Val021WideningTests.cs`.
- **Depends on.** T154.

### T159 [P]. Validator tests AC-8.4 (VAL022 state removal + VAL025 transition removal)
- **Acceptance.** Two fixtures:
  - `val022_state_removed.gravity` — `prev` has lifecycle states `{Submitted, Approved, Rejected}`, `next` has `{Submitted, Approved}`. Test asserts exactly one `VAL022` naming `Rejected` and severity `Error`.
  - `val025_transition_removed.gravity` — `prev` and `next` share identical state sets and event sets but `next` drops one transition (e.g. `Submitted -> Approved on Approved`). Test asserts exactly one `VAL025` and **severity is Warning** (not Error; FR-135).
  A third fixture `val022_state_added.gravity` adds a new state; test asserts zero diagnostics. Test framework in `Gravity.Dsl.Tests/Validation/Val022Val025Tests.cs`.
- **Files.** `tests/fixtures/versioning/validator/val022_state_removed.gravity`, `val022_state_added.gravity`, `val025_transition_removed.gravity`, `Gravity.Dsl.Tests/Validation/Val022Val025Tests.cs`.
- **Depends on.** T154.

### T160 [P]. Validator tests AC-8.5 (VAL023 command removal + VAL024 event removal)
- **Acceptance.** Three fixtures:
  - `val023_command_removed.gravity` — exactly one `VAL023` naming the removed command.
  - `val024_event_removed.gravity` — exactly one `VAL024` naming the removed event.
  - `val023_command_renamed.gravity` — `prev` has `Submit(...)`, `next` has `Send(...)` with the same args. Test asserts exactly one `VAL023` on `Submit` and **zero** `VAL023` on `Send` (the addition is not flagged unless VAL026 trips; pins AC-8.5 last sentence).
  Test framework in `Gravity.Dsl.Tests/Validation/Val023Val024Tests.cs`.
- **Files.** `tests/fixtures/versioning/validator/val023_command_removed.gravity`, `val023_command_renamed.gravity`, `val024_event_removed.gravity`, `Gravity.Dsl.Tests/Validation/Val023Val024Tests.cs`.
- **Depends on.** T154.

### T161 [P]. Validator tests AC-8.6 (VAL026 — four sub-causes + optional-add silent)
- **Acceptance.** Five fixtures:
  - `val026_arg_removed.gravity` — exactly one `VAL026` with sub-cause `"argument removed"`.
  - `val026_arg_renamed.gravity` — exactly one `VAL026` with sub-cause `"argument removed"` on the old name and one `VAL026` with sub-cause `"required argument added"` on the new name (when the new name is required). When the new name is optional, only the removal fires.
  - `val026_arg_narrowed.gravity` — exactly one `VAL026` with sub-cause `"argument type narrowed from <prev> to <next>"`. The rendered surface (e.g. `String?` → `String`) is asserted exactly.
  - `val026_required_added.gravity` — exactly one `VAL026` with sub-cause `"required argument added"`.
  - `val026_optional_added.gravity` — **zero** diagnostics (locked by AC-8.6 last sentence).
  Test framework in `Gravity.Dsl.Tests/Validation/Val026Tests.cs`.
- **Files.** Five `tests/fixtures/versioning/validator/val026_*.gravity` files, `Gravity.Dsl.Tests/Validation/Val026Tests.cs`.
- **Depends on.** T154.

### T162 [P]. Validator tests AC-8.9 (VAL028 + VAL029)
- **Acceptance.** Three fixtures:
  - `val028_deprecates_missing.gravity` — `Employee version 1 { ... }` plus `Employee version 2 deprecates version 9 until "2099-12-31" { ... }`. No `Employee@9`. Test asserts exactly one `VAL028` and **zero** `VAL027`.
  - `val029_self_reference.gravity` — `Employee version 2 deprecates version 2 until "2099-12-31"`. Test asserts exactly one `VAL029` and zero `VAL027`/`VAL028`.
  - `val029_forward_reference.gravity` — `Employee version 2 deprecates version 3 until "2099-12-31"` (with no `Employee@3` declared, but the `VAL029` predicate still trips because `3 >= 2`). Test asserts exactly one `VAL029`. (`VAL028` may or may not also fire depending on the chain-FQN's version index; pin the assertion to **at least one** `VAL029` and document the interaction.)
  Test framework in `Gravity.Dsl.Tests/Validation/Val028Val029Tests.cs`. The chain-skipped-link fixture `val027_skipped_link.gravity` (companion to T128's resolver fixture) lives here too and asserts exactly one `VAL027`, closing the second half of AC-8.8.
- **Files.** `tests/fixtures/versioning/validator/val027_skipped_link.gravity`, `val028_deprecates_missing.gravity`, `val029_self_reference.gravity`, `val029_forward_reference.gravity`, `Gravity.Dsl.Tests/Validation/Val028Val029Tests.cs`.
- **Depends on.** T154.

### T163 [P]. Validator tests AC-8.10 (VAL030 — strict less-than semantics)
- **Acceptance.** Paired test in `Gravity.Dsl.Tests/Validation/Val030Tests.cs`:
  - Fixture `val030_window.gravity` declares `Employee version 1 { ... }` plus `Employee version 2 deprecates version 1 until "2026-05-17"`. Test invokes `Validator.Validate(model, ..., currentDate: new DateOnly(2026, 5, 18))` and asserts exactly one `VAL030`.
  - Same fixture with `currentDate: new DateOnly(2026, 5, 17)` (equal to the `until` date) asserts **zero** `VAL030` (strict less-than per FR-138 last sentence).
  - Same fixture with `currentDate: new DateOnly(2025, 1, 1)` (before `until`) asserts zero `VAL030`.
  The compiler library is invoked directly — no `Program.cs`, no clock injection at the library layer (the test passes a deterministic `DateOnly` value). Closes AC-8.10 verbatim.
- **Files.** `tests/fixtures/versioning/validator/val030_window.gravity`, `Gravity.Dsl.Tests/Validation/Val030Tests.cs`.
- **Depends on.** T154.

### T164 [P]. Combined-fixture golden-file test AC-8.15 → `tests/golden/diagnostics/phase8/`
- **Acceptance.** Single fixture `tests/fixtures/versioning/validator/combined_all_rules.gravity` exercises VAL020, VAL021, VAL022, VAL023, VAL024, VAL025, VAL026, VAL027, VAL028, VAL029, VAL030 against a single resolved model (multiple FQNs, multiple version pairs, plus one expired deprecation window and one skipped chain link). The validator is invoked with a fixed `currentDate = 2026-05-18` so VAL030 fires deterministically. The expected diagnostic stream is rendered via the standard formatter (`path:line:col: <severity> <rule-id>: <message>`, locked by T053) and byte-checked against `tests/golden/diagnostics/phase8/combined.txt`. The golden file is committed in the same PR; any change requires `dotnet test -- --update-golden` and a deliberate update commit (same protocol as the C# emitter goldens locked by T048). FR-160 ordering is what makes the byte-check stable; if any rule reorders, this test fails and surfaces the regression.
- **Files.** `tests/fixtures/versioning/validator/combined_all_rules.gravity`, `tests/golden/diagnostics/phase8/combined.txt`, `Gravity.Dsl.Tests/Validation/Phase8DiagnosticGoldenTests.cs`.
- **Depends on.** T155, T156, T157, T158, T159, T160, T161, T162, T163.

### T165. CLI: add `--as-of YYYY-MM-DD` flag to `gravc gen` and `gravc check` (FR-141)
- **Acceptance.** `Gravity.Dsl.Cli/Program.cs`'s argument parser (line ~90, per plan.md §3.10) gains a `--as-of <value>` case for both subcommands (`gen` and `check`). The flag is captured into `parsed.AsOfRaw` as a `string?`. Help text on both subcommands documents the flag, its `YYYY-MM-DD` format, the default behaviour (computed from `DateTime.UtcNow` when absent), and that the value is logged in verbose mode for reproducibility. The CLI-only flag does not surface in any compiler library — purity per FR-140 / LD-7.
- **Files.** `Gravity.Dsl.Cli/Program.cs`, `Gravity.Dsl.Cli/Commands/GenCommand.cs`, `Gravity.Dsl.Cli/Commands/CheckCommand.cs`.
- **Depends on.** —

### T166. CLI: resolve `--as-of` to a `DateOnly`, default to `DateOnly.FromDateTime(DateTime.UtcNow)` in `Program.cs` (FR-141)
- **Acceptance.** `Program.cs::Main` resolves the `DateOnly` exactly as in plan.md §3.10:
  - When `parsed.AsOfRaw is { Length: > 0 } raw`, parse with `DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf)`. On parse failure, emit `gravc CLI002: --as-of value '<raw>' must be YYYY-MM-DD` to `Console.Error` and return non-zero (CLI002 emission lands under T167).
  - When `parsed.AsOfRaw is null`, compute `asOf = DateOnly.FromDateTime(DateTime.UtcNow)`.
  This is the **only** `DateTime.UtcNow` call in the entire repo. The call is documented inline with the verbatim FR-141 comment block (per plan.md §3.10). `Directory.Build.props` is updated to conditionally attach `BannedSymbolsFile` to every project **except** `Gravity.Dsl.Cli` (per plan.md §6 risk-register mitigation), via `<Choose>/<When Condition="'$(MSBuildProjectName)' != 'Gravity.Dsl.Cli'">`. CI lint `rg "DateTime\.\(UtcNow\|Now\)" Gravity.Dsl.Compiler Gravity.Dsl.Ast Gravity.Dsl.Emitter Gravity.Dsl.Emitter.CSharp` returns zero matches (literal source-text check; pinned per plan.md §4(e)).
- **Files.** `Gravity.Dsl.Cli/Program.cs`, `Directory.Build.props`.
- **Depends on.** T165.

### T167. CLI: `CLI002` on malformed `--as-of` (FR-142)
- **Acceptance.** Malformed `--as-of` values emit `gravc CLI002: --as-of value '<raw>' must be YYYY-MM-DD` to `Console.Error` before any compilation work begins (parser+resolver+validator are not invoked). Exit code is non-zero (returns 1). The rule id `CLI002` lives in the CLI binary, not in `Gravity.Dsl.Compiler/Validation/RuleIds.cs` (FR-142 is explicit on this — CLI-only rule). The CLI does not introduce its own rule-id constants file unless one already exists; if not, declare `internal const string Cli002 = "CLI002"` in `Gravity.Dsl.Cli/CliRuleIds.cs` (new) for symmetry with the compiler rule-id surface. Negative AC-8.11 case (`--as-of 2026-13-45`) closes here.
- **Files.** `Gravity.Dsl.Cli/Program.cs`, `Gravity.Dsl.Cli/CliRuleIds.cs` (new).
- **Depends on.** T166.

### T168. CLI: thread `currentDate` through `CompilerPipeline` to `Validator`
- **Acceptance.** `Gravity.Dsl.Cli/CompilerPipeline.cs::Check` and `::Gen` each gain a `DateOnly currentDate` parameter (no default — the compile breaks if a caller forgets to thread it; plan.md §3.10). Both methods pass `currentDate` directly to `Validator.Validate(model, claimedAnnotationNamespaces, currentDate)`. `Program.cs::Main` passes the resolved `asOf` (from T166) into both pipeline entry points. No clock read anywhere in `CompilerPipeline`. Existing test harnesses in `Gravity.Dsl.Tests/` that drive the pipeline directly (the smoke tests under T052) are migrated to pass `default(DateOnly)` or a fixed value; same migration as T154 callers.
- **Files.** `Gravity.Dsl.Cli/CompilerPipeline.cs`, `Gravity.Dsl.Cli/Program.cs`, `Gravity.Dsl.Tests/**` callers of the pipeline.
- **Depends on.** T154, T166.

### T169 [P]. CLI integration test AC-8.11 (`--as-of` plumbing + `CLI002` negative)
- **Acceptance.** `Gravity.Dsl.Tests/Cli/AsOfFlagTests.cs` (per plan.md §5.4) invokes `CompilerPipeline.Check` and `CompilerPipeline.Gen` in-process against fixtures under `tests/fixtures/versioning/cli_as_of/`:
  1. **In-window positive** — `--as-of 2099-01-01` against a fixture whose `until = "2026-12-31"`. Exit 0; zero `VAL030`.
  2. **Out-of-window** — `--as-of 2099-01-01` against a fixture whose `until = "2098-12-31"`. Exit non-zero; exactly one `VAL030`.
  3. **Malformed flag** — `--as-of 2026-13-45`. Exit non-zero; `CLI002` written to the stderr buffer; no compilation work runs (the pipeline is not invoked).
  4. **Default clock sanity** — no `--as-of`, fixture `until = "9999-12-31"`. Exit 0 (default clock is far before 9999). The default-clock branch is exercised but the diagnostic count is the assertion, not the clock value itself.
  Each test uses an in-process `Program.Main` invocation with stdout/stderr buffered into `StringWriter`s so assertions are deterministic. AC-8.11 closes here.
- **Files.** Three `tests/fixtures/versioning/cli_as_of/*.gravity` fixtures, `Gravity.Dsl.Tests/Cli/AsOfFlagTests.cs`.
- **Depends on.** T167, T168.

---

## Sub-phase P8d — Sample fixtures (T170–T172)

Goal: exercise the multi-version surface against the existing `samples/registry/` set so the curated samples remain a working showcase of the language as it evolves. Closes the sample-coverage half of AC-8.7 (the resolver tests already pin the in-tree behaviour; this sub-phase makes the curated samples a live demonstration). Closes no formal AC on its own — fixtures here are supplemental to the Phase 0–3 sample set locked by T003.

### T170 [P]. Add v2 sample entities exercising chained `deprecates`
- **Acceptance.** Under `samples/registry/`, add v2 versions of at least `Employee` and `Project` (the predecessor's curated entities from T003), each carrying `deprecates version 1 until "<a future date>"`. The v2 surfaces add fields (additive) and exercise the chained-coexistence path end-to-end through the C# emitter (T040..T049). Concretely:
  - `samples/registry/Employee.gravity` carries both `entity Employee version 1 { ... }` and `entity Employee version 2 deprecates version 1 until "2099-12-31" { ... }` in the same file (or as two files under `samples/registry/v2/` — pick the lower-friction layout that still makes the chain visible). The v2 surface adds one or two new properties to demonstrate additive evolution; it does not remove or narrow anything (no `VAL020..VAL026` should fire).
  - `samples/registry/Project.gravity` mirrors the pattern with `Project@1` and `Project@2 deprecates 1`. A consuming property on `TimeEntry` (a relation or a property of type `Project`) is left unqualified to exercise the FR-126 max-version resolution path through the emitter.
  No new emitter rules are introduced; this task only exercises the existing surface.
- **Files.** `samples/registry/Employee.gravity`, `samples/registry/Project.gravity` (or `samples/registry/v2/*.gravity` if a separate subdirectory is cleaner).
- **Depends on.** T154.

### T171 [P]. Update `samples/registry/.gravity.config` if needed
- **Acceptance.** Inspect `samples/registry/.gravity.config` (locked by T003 for the `csharp`-only emitter set). If the v2 samples from T170 require any config delta (e.g. an output sub-directory split per version, or a new namespace mapping), apply the minimum-surface change. If no config change is needed, this task is a no-op verified by re-reading the file and documenting "no delta required" in the closing PR description. The `csharp` emitter remains the only enabled emitter (no Phase 4–7 emitters yet; LD-8).
- **Files.** `samples/registry/.gravity.config` (touched only if needed).
- **Depends on.** T170.

### T172 [P]. Ensure samples still pass `gravc check` and `gravc gen --emitter csharp`
- **Acceptance.** A CI step (or an existing smoke test extended) runs `gravc check --input samples/registry` and `gravc gen --input samples/registry --output out/csharp --emitter csharp` against the updated sample set. Both commands exit 0 with zero error-severity diagnostics (warnings such as `VAL025` may surface if a v2 sample tightens a lifecycle; per AC-8.4 these are warnings, not errors, and do not fail the build). The generated C# under `out/csharp/` compiles in the existing T052 throwaway harness. The T048 golden-file baseline is updated only if the samples' Phase 0–3 emitter surface intentionally changes; if a golden file changes, the change is deliberate and reviewed (same protocol as the T048 golden update process). Reviewers should expect new `<Entity>@2` artifacts to land alongside `<Entity>@1`; the C# emitter iterates `Declarations` in `(Fqn ordinal, Version asc)` order (FR-161) so file names and contents are deterministic.
- **Files.** Possibly `Gravity.Dsl.Tests/EndToEnd/SmokeTests.cs` (extend or add a multi-version variant), possibly `tests/golden/csharp/**` (only on deliberate, reviewed updates).
- **Depends on.** T170, T171.

---

## Phase gate summary

| Sub-phase | Closing tasks | Spec ACs satisfied |
|---|---|---|
| P8a | T100–T111 | AC-8.12, AC-8.14, AC-8.16 |
| P8b | T120–T129 | AC-8.7, AC-8.8 (`RES004` half), AC-8.13 |
| P8c | T140–T169 | AC-8.1, AC-8.2, AC-8.3, AC-8.4, AC-8.5, AC-8.6, AC-8.8 (`VAL027` half), AC-8.9, AC-8.10, AC-8.11, AC-8.15 |
| P8d | T170–T172 | (samples; AC-8.7 also via samples) |

Cross-phase notes:
- **AC-8.8** is split deliberately across P8b and P8c. The resolver fires `RES004` at the unchained gap (T124 + T128); the validator fires `VAL027` at the skipped link (T150 + T162). The same fixture pins both halves; the assertions are independent so a regression in either half is caught in isolation.
- **AC-8.14** has both an AST-version literal assertion (closed in T100 — `AstVersion.Value == "1.1.0"`) and a third-party-emitter-loading assertion (closed in T111 — vendored 1.0.0 assembly loads against the 1.1.0 host). Both halves are required for AC-8.14 closure.
- **FR-150** (diagnostic shape: rule id, span, FQN-and-version, optional hint) is satisfied transitively by every VAL/RES/PARSE emission site listed under T104..T153; no standalone task is required because the diagnostic constructor already enforces span attachment and the message-body conventions are pinned per-rule in this task list.
- **FR-160** ordering on the Phase 8 block is closed by T155; AC-8.15's combined-fixture golden test (T164) is what locks it in CI.
- **FR-161** ordering on the `(FQN, Version)`-keyed declaration map is closed by T120 + T121 (the `DeclKeyComparer.Instance` is the single source of truth; iteration is `(Fqn ordinal asc, Version asc)`).

## Revision history

- 2026-05-18 — Initial lock. Phase 8 task plan authored against `spec.md` FR-100..FR-161 and `plan.md` sub-phases P8a / P8b / P8c / P8d.
- 2026-05-18 — Critic-pass fixes: T101 path corrected to `Gravity.Dsl.Tests/Helpers/SpanIgnoringEquality.cs` (the file at `Gravity.Dsl.Compiler/Equality/` does not exist); `NamedTypeRef.Version` positional moved to LAST with default `null` to preserve `1.0.0` ctor compatibility; T102 enumeration tightened to the single grep-verified production site at `Parser.cs:640`; T106a added for `PARSE020` on `@N` after `returns` (FR-100) with companion negative case in T109; T123 reworked: `VersionIndex` is an init-only property on `ResolvedModel`, not a new positional record argument; T124a inserted for the RES004 message-body migration grep audit; T125 pinned to imports-transitive scope per FR-126; T129 picks the `Resolver.ResolveWithBindings` test-only overload + adds the cross-file AC-8.13b fixture.
