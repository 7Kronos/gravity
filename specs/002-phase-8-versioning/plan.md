# Gravity DSL — Implementation Plan (Phase 8: Additive-only versioning)

**Status:** Locked for implementation
**Date:** 2026-05-18
**Driven by:** `specs/002-phase-8-versioning/spec.md` and `CLAUDE.md` (Principle IV dominant).

---

## 1. Strategy

Three scoped sub-phases executed sequentially. Each sub-phase has a single hand-off: its outputs unblock the next, but no sub-phase reaches the validator's breaking-change pass until the AST and resolver layers are stable. P8a is a grammar+AST change with zero semantic effect; P8b is the resolver upgrade that unlocks multi-version coexistence; P8c is the diff engine plus the CLI wiring that makes deprecation windows date-driven.

| Sub-phase | Output | Gate (spec ACs and FRs closed) |
|---|---|---|
| P8a. Grammar + AST | `NamedTypeRef.Version` field; `@N` lex/parse production with `PARSE020` errors; `SourceWriter` emits `@N`; `AstVersion` bumped to `1.1.0`; AC-8.14 / AC-8.16 round-trip extended. | FR-100, FR-101, FR-102, FR-110, FR-111, FR-112, FR-113, FR-151 (PARSE020). AC-8.12, AC-8.14, AC-8.16. |
| P8b. Resolver multi-version | `DeclKey(Fqn, Version)`; `ResolvedModel.Declarations` re-keyed; deprecates-chain admission of coexisting versions; missing-version variant of `RES003`; chain checks at the resolver layer. | FR-120, FR-121, FR-122, FR-123, FR-126, FR-127, FR-161. AC-8.7, AC-8.8 (RES004 half), AC-8.13. |
| P8c. Validator diff + CLI | Eight new validator rules (VAL020..VAL030); `Validator.Validate(..., DateOnly currentDate)`; CLI `--as-of` plumbing; deterministic diagnostic ordering. | FR-124, FR-125, FR-130, FR-131, FR-132, FR-133, FR-134, FR-135, FR-136, FR-137, FR-138, FR-140, FR-141, FR-142, FR-150, FR-160. AC-8.1, AC-8.2, AC-8.3, AC-8.4, AC-8.5, AC-8.6, AC-8.8 (VAL027 half), AC-8.9, AC-8.10, AC-8.11, AC-8.15. |

Out-of-scope phases (4–7 emitters, 9–10 build integration + OSS launch) are referenced but not addressed. Per LD-8 they ship after this phase against the versioning-aware AST.

## 2. Project layout

Mirrors the predecessor plan; restricted to additions and touches required by Phase 8.

```
Gravity.Dsl/
├── Gravity.Dsl.Ast/
│   ├── Types/NamedTypeRef.cs                  # TOUCH: int? Version = null (positional, LAST, default null — preserves 1.0.0 ctor)
│   └── AstVersion.cs                          # TOUCH: "1.0.0" -> "1.1.0"
├── Gravity.Dsl.Compiler/
│   ├── Parsing/Parser.cs                      # TOUCH: ParseTypeRef accepts @N suffix
│   ├── Parsing/RuleIds.cs                     # TOUCH: add Parse020 constant
│   ├── Parsing/SourceWriter.cs                # TOUCH: WriteTypeRef emits @N
│   ├── Resolution/Resolver.cs                 # TOUCH: DeclKey-based maps, chain admission, RES003 variant
│   ├── Resolution/ResolvedModel.cs            # TOUCH: Declarations keyed by DeclKey
│   ├── Validation/Validator.cs                # TOUCH: currentDate parameter; drives VersionDiff
│   ├── Validation/RuleIds.cs                  # TOUCH: Val020..Val030 constants
│   ├── Versioning/DeclKey.cs                  # NEW: (string Fqn, int Version) record + comparer
│   ├── Versioning/VersionDiff.cs              # NEW: per-pair diff engine, eight rule entry points
│   ├── Versioning/DiffRules.cs                # NEW: VAL020..VAL026, VAL027, VAL028, VAL029, VAL030 rule bodies
│   └── Versioning/Narrowing.cs                # NEW: IsNarrowing(TypeRef prev, TypeRef next) per FR-131
├── Gravity.Dsl.Cli/
│   ├── CompilerPipeline.cs                    # TOUCH: thread DateOnly currentDate through Check + Gen
│   └── Program.cs                             # TOUCH: --as-of flag; CLI002 on malformed value; UTC clock read at this single site
├── samples/registry/v2/
│   ├── Employee.gravity                       # NEW: Employee@1 + Employee@2 deprecates chain
│   ├── Project.gravity                        # NEW: Project@1 + Project@2 + qualified type-ref consumers
│   └── .gravity.config                        # NEW: same csharp emitter config as v1
├── tests/fixtures/versioning/
│   ├── parse/                                 # @N parse positive + negative cases (FR-100, FR-101)
│   ├── resolver/                              # chain admission, missing-version, RES003 variant
│   └── validator/                             # one fixture per VAL020..VAL030 sub-cause + combined
└── tests/golden/diagnostics/phase8/           # NEW: byte-checked diagnostic outputs (AC-8.15)
```

NuGet boundaries are unchanged. `Gravity.Dsl.Compiler/Versioning/*` is `internal` to the compiler assembly; nothing in the AST package depends on it. The new versioning layer is a sibling to `Resolution` and `Validation`, deliberately not folded into either, so the diff engine can be unit-tested without standing up a full `ResolvedModel`.

## 3. Module-level architecture

### 3.1 `DeclKey` (`Gravity.Dsl.Compiler/Versioning/DeclKey.cs`)

```csharp
internal readonly record struct DeclKey(string Fqn, int Version)
    : IComparable<DeclKey>
{
    public int CompareTo(DeclKey other)
    {
        int c = string.CompareOrdinal(Fqn, other.Fqn);
        return c != 0 ? c : Version.CompareTo(other.Version);
    }
}

internal sealed class DeclKeyComparer : IComparer<DeclKey>
{
    public static readonly DeclKeyComparer Instance = new();
    public int Compare(DeclKey x, DeclKey y) => x.CompareTo(y);
}
```

`readonly record struct` gives us value equality without allocating on every map lookup. `Fqn` ordering is `string.CompareOrdinal` so the iteration order matches the Phase 0–3 ordinal contract on the existing string-keyed map. `Version` is an ascending tie-breaker, which is the order required by FR-161 and the order the diff engine walks chains in.

### 3.2 `ResolvedModel.Declarations` migration

The map's key type changes from `string` to `DeclKey`. A new `VersionIndex` is added as an **init-only property** (NOT a new positional record argument — this preserves the `ResolvedModel` primary constructor's arity for any downstream caller):

```csharp
public sealed record ResolvedModel(
    ImmutableSortedDictionary<DeclKey, TopLevelDecl> Declarations,
    IReadOnlyList<SourceFile> Files,
    IReadOnlyDictionary<string, ImmutableSortedDictionary<string, TopLevelDecl>> FileImports)
{
    public ImmutableDictionary<string, ImmutableArray<int>> VersionIndex { get; init; }
        = ImmutableDictionary<string, ImmutableArray<int>>.Empty;
}
```

`FileImports` keeps its `string`-keyed inner map: per-file simple-name scopes resolve to a single canonical declaration per simple name (the max-version one for unqualified refs). `VersionIndex` (FQN → versions ascending) is computed once during `Resolver.Resolve` and set via object-initializer at construction; consumers include the validator's breaking-change pass (T142, T150) and tests that need to inspect coexistence. Emitters iterate `Declarations` (now version-aware) or look up by simple name (Phase 0–3 contract preserved).

Every caller of `model.Declarations` needs migration. The complete list, from a `grep "model.Declarations\|\.Declarations" Gravity.Dsl.*`, is:

| Caller | Current shape | Migration |
|---|---|---|
| `Gravity.Dsl.Compiler/Validation/Validator.cs:30` | `foreach (var kv in model.Declarations)` then `kv.Value` is `TopLevelDecl` | `kv.Key` is now `DeclKey`; pattern is unchanged because the loop body only reads `kv.Value`. |
| `Gravity.Dsl.Emitter.CSharp/CSharpEmitter.cs:57` | `foreach (var kv in model.Declarations)` | Same as above; the emitter iterates in `(Fqn, Version asc)` order, which is the FR-161 contract. |
| `Gravity.Dsl.Tests/Stubs/NoopEmitter.cs:26` | `foreach (var kv in model.Declarations)` | Same as above. |
| `Gravity.Dsl.Compiler/Resolution/Resolver.cs:259` | `new ResolvedModel(declMap.ToImmutable(), ...)` where `declMap` is `string`-keyed | Builder changes type to `DeclKey`-keyed; see §3.4. |

No caller uses positional record patterns on `ResolvedModel`; no caller looks up by FQN string today (relation/property name resolution goes through `FileImports`). The migration is therefore a `Dictionary<string, T>` → `Dictionary<DeclKey, T>` mechanical change at the construction site plus a one-line tweak at every iterating consumer (no loop body changes).

### 3.3 `NamedTypeRef` record shape

Before:

```csharp
public sealed record NamedTypeRef(
    string Name, bool IsOptional, bool IsArray, SourceSpan Span)
    : TypeRef(Span);
```

After (FR-110):

```csharp
public sealed record NamedTypeRef(
    string Name, bool IsOptional, bool IsArray, SourceSpan Span, int? Version = null)
    : TypeRef(Span);
```

Positional order rationale (revised after critic pass): `Version` is appended as the **last** positional parameter with a default of `null`. This preserves the `1.0.0` 4-argument constructor signature: existing source code that did `new NamedTypeRef(name, isOpt, isArr, span)` continues to compile and runs identically against the new record (the default kicks in, `Version = null`). The previously-considered alternative (insert `Version` at index 1) was rejected because it changes constructor arity in a way that breaks the FR-111 backward-compat promise. The "silent drop" failure mode that the earlier rationale warned about cannot occur in practice: no `1.0.0` source code ever constructed a `NamedTypeRef` with a version argument, because the `@N` grammar did not exist; the only construction site that needs to pass `version` is the new parser code, which always does so explicitly.

All structural pattern sites grepped earlier (`Renderers.cs`, `TypeMapper.cs`, `Resolver.cs:272`, `SourceWriter.cs:284`, `Gravity.Dsl.Tests/Helpers/SpanIgnoringEquality.cs:150`) use the `is NamedTypeRef n` form and then read named properties — zero positional deconstruction sites. They compile against the new shape without any pattern rewrites. The Parser's constructor call at `Parser.cs:640` becomes `new NamedTypeRef(nameTok.Lexeme, isOptional, isArray, nameTok.Span, version)`. AC-8.14's stability test confirms that a `1.0.0`-compiled emitter assembly continues to load against `1.1.0`.

### 3.4 Resolver upgrade

`Resolver.Resolve(IReadOnlyList<SourceFile> files, string inputRoot)` keeps its external signature (FR-126/FR-127 are internal to its body). Internally it changes in four ways.

**(a) Decl map is `DeclKey`-keyed.** The builder becomes `ImmutableSortedDictionary.CreateBuilder<DeclKey, TopLevelDecl>(DeclKeyComparer.Instance)`. The duplicate check (FR-121) collapses to `if (declMap.ContainsKey(new DeclKey(fqn, decl.Version)))` and still emits `RES004` with the unchanged Phase 0–3 message for the duplicate-version case.

**(b) Version index.** A new helper `BuildVersionIndex(declMap)` returns `ImmutableSortedDictionary<string, ImmutableArray<int>>` (versions ascending per FQN). This is the lookup table the chain validator and FR-126's "resolve unqualified to max" both consume.

**(c) Chained admission of coexisting versions (FR-122 / FR-123).** Pseudocode:

```
groups := declMap grouped by Fqn
for each fqn, versions in groups where versions.Count > 1:
    versions := versions sorted ascending
    for i in 1..versions.Count-1:
        prev := versions[i-1]
        next := versions[i]
        nextDecl := declMap[(fqn, next)]
        if nextDecl is EntityDecl ent and ent.Deprecates?.Version == prev:
            // chain holds at this link; nothing to emit
            continue
        else:
            emit RES004 at nextDecl.Span:
              "entity '{fqn}' is declared more than once; multi-version coexistence
               requires a deprecates chain"
```

(`ValueTypeDecl` and `EnumDecl` do not carry `DeprecatesClause` in the v1 grammar, so for those `TopLevelDecl` kinds a second version is always `RES004`.) FR-122's "immediately-preceding version" check is enforced by walking adjacent pairs of the ascending list; a skipped link (`v3 deprecates v1` while `v2` coexists) falls out as `RES004` on the `(v2, v3)` adjacency where the chain check fails, and is **also** picked up by FR-137 / `VAL027` in the validator pass — the resolver fires `RES004` at the gap and the validator fires `VAL027` at the broken-chain instance; the AC-8.8 fixtures pin both behaviours independently.

**(d) Missing-version variant of `RES003` (FR-127) + scoped-max semantics (FR-126).** `CheckTypeRef` becomes version-aware. When `named.Version` is `null`, the resolver invokes a `BindUnqualified(typeName, fpath)` helper that consults `VersionIndex[fqn]` **filtered to versions whose declaring file is `fpath` itself or transitively imported by `fpath`** — this pins the FR-126 "scope means imports-transitive" interpretation against the alternate "max in model" reading. AC-8.13b exercises a cross-file fixture where v2 is declared but not imported by the consuming file and must resolve to v1. When `named.Version is { } v`, the resolver consults the same imports-filtered version index for the FQN bound to that simple name in scope and emits one of:

```
"type '{name}@{v}' is not declared; '{name}' exists with versions {list}"
"name '{name}' is not defined or imported in this scope"
```

The list is rendered as `1, 2` (comma-space, ascending, ordinal). The non-versioned path remains exactly the Phase 0–3 message so RES003 consumers that grep on the old text are unaffected.

The chained-admission validator is structured so its diagnostics are emitted in `(Fqn ordinal, Version ascending, span)` order; this is the same order FR-160 requires for the validator pass, which means the merged diagnostic list does not need a re-sort at the resolver / validator boundary.

**(e) Test-only `ResolveWithBindings` overload.** A new internal overload `Resolver.ResolveWithBindings(IReadOnlyList<SourceFile> files, string inputRoot)` returns a tuple `(ResolveResult result, IReadOnlyDictionary<NamedTypeRef, DeclKey> bindings)`. The `bindings` map exposes the resolver's internal type-ref-to-decl binding for tests that need to assert (e.g. AC-8.13) that an unqualified `Project` bound to `Project@2`. This keeps the public `ResolvedModel` shape stable (no new positional record argument, no public-API drift on the compiler library) while giving the test surface a deterministic assertion handle. The overload is `internal` and exposed to `Gravity.Dsl.Tests` via `[InternalsVisibleTo]`.

### 3.5 Parser: `@N` suffix on `NamedTypeRef`

`Lexer` already produces `TokenKind.At` (used by annotations). No lexer change; the suffix recognition lives entirely in `ParseTypeRef(ParserState s)` between the identifier read and the `?`/`[]` modifiers.

Insertion point: `Gravity.Dsl.Compiler/Parsing/Parser.cs` line 596, immediately after `var nameTok = s.Expect(TokenKind.Identifier);` and before the `?`/`[]` block at line 599.

```csharp
int? version = null;
if (s.Peek().Kind == TokenKind.At)
{
    var atTok = s.Consume();
    // FR-101: malformed suffix emits PARSE020 at the '@' token and recovers by
    // treating the @ as unconsumed input (we already consumed it, but we
    // continue the type-ref production as if Version is null so a single
    // malformed suffix does not cascade).
    if (!TryReadVersionSuffix(s, atTok, out version, out var diag))
    {
        s.AddDiagnostic(diag);
        version = null;
    }
}
```

`TryReadVersionSuffix` is a private helper in `Parser.cs`. It peeks for an `IntegerLiteral` token immediately adjacent (no intervening whitespace — tracked via `s.Peek().Span.Column == atTok.Span.Column + 1`; the lexer already preserves columns), parses the lexeme with `int.TryParse(..., NumberStyles.None, CultureInfo.InvariantCulture, out var n)`, and rejects: missing literal (`PARSE020`: "expected positive integer after '@'"), leading zero (`PARSE020`: "version suffix must not have a leading zero"), non-positive (`PARSE020`: "version suffix must be a positive integer"), whitespace (column-adjacency check). `int.TryParse(NumberStyles.None)` rejects leading `+` and `-` automatically.

The primitive-vs-named decision at line 624 must consider `version`: if `prim is not null` and `version is not null`, emit `PARSE020` ("version suffix is not permitted on primitive types") and discard `version`. The relation-target parser at lines 324 and 410 calls `ParseTypeRef` indirectly via the relation parser; the relation parser already constrains the target to a single identifier without a `?`/`[]` modifier, so the same constraint extends to `@N` — when a relation target is followed by `@`, the relation parser emits `PARSE020` ("version suffix is not permitted on relation targets") and skips the suffix. AC-8.12 covers both refusal sites.

### 3.6 `SourceWriter`

`WriteTypeRef` in `Gravity.Dsl.Compiler/Parsing/SourceWriter.cs:276` gains a single line in the `NamedTypeRef` arm:

```csharp
case NamedTypeRef n:
    sb.Append(n.Name);
    if (n.Version is { } v)
    {
        sb.Append('@').Append(v.ToString(CultureInfo.InvariantCulture));
    }
    WriteTypeSuffix(sb, n.IsOptional, n.IsArray);
    break;
```

Order matters: `@N` is appended after the name and **before** `?`/`[]`, matching the grammar (`Foo@2?[]`). `int.ToString(CultureInfo.InvariantCulture)` keeps determinism; the constitution's banned-APIs list permits `int.ToString(IFormatProvider)`. The round-trip harness (AC-8.16) gains two fixtures under `tests/fixtures/parser/` that exercise `Money@2`, `Money@2?`, `Money@2[]`, `Money@2?[]` to pin the modifier interaction.

### 3.7 Validator entry-point signature

Before:

```csharp
public static IReadOnlyList<Diagnostic> Validate(
    ResolvedModel model,
    IReadOnlyCollection<string> claimedAnnotationNamespaces);
```

After (FR-140):

```csharp
public static IReadOnlyList<Diagnostic> Validate(
    ResolvedModel model,
    IReadOnlyCollection<string> claimedAnnotationNamespaces,
    DateOnly currentDate);
```

`DateOnly` is in `System` (BCL) and pure value-typed; no clock read inside the compiler library. All callers are migrated:

| Caller | File | Update |
|---|---|---|
| `RunCheck` pipeline | `Gravity.Dsl.Cli/CompilerPipeline.cs:53` | Pass `parsed.AsOf` (threaded from CLI args). |
| `RunGen` pipeline | `Gravity.Dsl.Cli/CompilerPipeline.cs:87` | Same. |
| `ValidatorTests` harness | `Gravity.Dsl.Tests/Validation/ValidatorTests.cs:23` | Add a `DateOnly currentDate = default` parameter; tests that exercise `VAL030` pass a deterministic value. |

The default `DateOnly` value (`0001-01-01`) is **before** any reasonable `until` date, so by FR-138's strict-less-than semantics the deprecation-window check never fires when the default is used. Existing Phase 0–3 validator tests that do not exercise versioning therefore pass unchanged.

### 3.8 `VersionDiff`: the diff engine

`Gravity.Dsl.Compiler/Versioning/VersionDiff.cs` is invoked by `Validator.Validate` after the existing per-declaration rules run. It walks `model.Declarations` grouped by `DeclKey.Fqn`, builds adjacent `(Vprev, Vnext)` pairs that are chained, and runs the eight diff rules for each pair. Pseudocode:

```
groups := model.Declarations grouped by Key.Fqn, values sorted by Key.Version asc
for each (fqn, ordered) in groups:
    for i in 1..ordered.Count-1:
        prev := ordered[i-1]
        next := ordered[i]
        if not Chained(prev, next): continue           // already RES004'd at resolver
        foreach rule in DiffRules.All:
            rule.Apply(prev, next, sink)
DiffRules.ApplyAcrossModel(model, sink)               // VAL027, VAL028, VAL029 are per-decl, not per-pair
DiffRules.ApplyWindow(model, currentDate, sink)       // VAL030
sink.SortAndFlush()                                    // FR-160 ordering
```

`sink` is a `DiagnosticSink` that accumulates and finally sorts by `(Fqn ordinal, Vnext asc, RuleId ordinal, Span.Path ordinal, Span.Line, Span.Column)`. The order matches FR-160 exactly. For per-decl rules (VAL028, VAL029, VAL030) where there is no `Vnext`, the secondary key is the decl's own `Version`. The sort is stable so duplicate keys preserve insertion order (which itself comes from the iteration of `ImmutableSortedDictionary`, already deterministic).

Reference rule body (VAL020 — field removed; same pattern repeated by VAL022..VAL024 with different container types):

```csharp
internal static void ApplyVal020(
    TopLevelDecl prev, TopLevelDecl next, DiagnosticSink sink)
{
    if (prev is EntityDecl ep && next is EntityDecl en)
    {
        // entity properties
        DiffByName(
            ep.Properties.Select(p => p.Name),
            en.Properties.Select(p => p.Name),
            removedName => sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val020,
                $"entity-property.{removedName} was removed in {Fqn(en)}@{en.Version}; " +
                "field removal is a breaking change",
                en.Span)));
        // event payloads
        foreach (var prevEvt in ep.Events)
        {
            var nextEvt = en.Events.FirstOrDefault(e => e.Name == prevEvt.Name);
            if (nextEvt is null) continue;             // event removal: handled by VAL024
            DiffByName(
                prevEvt.Payload.Select(f => f.Name),
                nextEvt.Payload.Select(f => f.Name),
                removed => sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val020,
                    $"event-payload.{prevEvt.Name}.{removed} was removed in " +
                    $"{Fqn(en)}@{en.Version}; field removal is a breaking change",
                    nextEvt.Span)));
        }
        // command arguments: VAL026, not VAL020 (the command surface is a contract on its own)
    }
    else if (prev is ValueTypeDecl vp && next is ValueTypeDecl vn)
    {
        DiffByName(
            vp.Fields.Select(f => f.Name),
            vn.Fields.Select(f => f.Name),
            removed => sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val020,
                $"value-type-field.{removed} was removed in {Fqn(vn)}@{vn.Version}; " +
                "field removal is a breaking change",
                vn.Span)));
    }
}
```

`DiffByName` is a helper that streams `prev.Except(next, StringComparer.Ordinal)` ordered by the `prev` sequence; this guarantees diagnostic order matches declaration order in `Vprev`, which is determined and stable. Container labels (`entity-property`, `value-type-field`, `event-payload`, `command-argument`) are constants in `DiffRules`, satisfying FR-130's container-naming requirement.

### 3.9 `Narrowing` table (FR-131)

`Gravity.Dsl.Compiler/Versioning/Narrowing.cs`:

```csharp
internal static class Narrowing
{
    public static bool IsNarrowing(TypeRef prev, TypeRef next)
    {
        // 1. Optionality lost.
        if (Opt(prev) && !Opt(next)) return true;
        // 2. Array-ness lost.
        if (Arr(prev) && !Arr(next)) return true;
        // 3 + 4. Same-kind primitive: not narrowing.
        if (prev is PrimitiveTypeRef pp && next is PrimitiveTypeRef pn)
        {
            return IsPrimitiveNarrowing(pp.Kind, pn.Kind);
        }
        // 5. Named-named: name change is a rename (handled by VAL020 add+remove, not VAL021).
        if (prev is NamedTypeRef np && next is NamedTypeRef nn)
        {
            if (!string.Equals(np.Name, nn.Name, StringComparison.Ordinal))
                return false;                          // VAL020 path, not VAL021
            // Version-decrease narrows (FR-131 last bullet).
            return (np.Version ?? int.MaxValue) > (nn.Version ?? int.MaxValue);
        }
        // 6. Cross-kind (Primitive <-> Named): always narrowing on the primitive side
        //    because the named ref's shape is unknown to the diff engine and the
        //    safest assumption per Principle IV is "this is a contract change".
        return prev is PrimitiveTypeRef ^ next is PrimitiveTypeRef;
    }

    private static bool IsPrimitiveNarrowing(PrimitiveKind prev, PrimitiveKind next)
    {
        if (prev == next) return false;
        // Closed-form narrowing table per FR-131 (revised after critic pass):
        // ONLY the explicit pairs below are narrowing. Every other pair (including
        // unspecified mixed pairs like UUID->String) returns false. The previous
        // "fallthrough narrows" rule was inverted to "fallthrough does NOT narrow"
        // to match the constitution's bias toward permissive widening and to make
        // the table provably complete via the AC-8.2 enumeration.
        return (prev, next) switch
        {
            (PrimitiveKind.Decimal, PrimitiveKind.Int) => true,
            (PrimitiveKind.Decimal, PrimitiveKind.Long) => true,
            (PrimitiveKind.Long, PrimitiveKind.Int) => true,
            (PrimitiveKind.DateTime, PrimitiveKind.Date) => true,
            (PrimitiveKind.String, PrimitiveKind.Int) => true,
            (PrimitiveKind.String, PrimitiveKind.Long) => true,
            (PrimitiveKind.String, PrimitiveKind.Decimal) => true,
            (PrimitiveKind.String, PrimitiveKind.Boolean) => true,
            (PrimitiveKind.String, PrimitiveKind.Uuid) => true,
            (PrimitiveKind.String, PrimitiveKind.Date) => true,
            (PrimitiveKind.String, PrimitiveKind.DateTime) => true,
            _ => false,
        };
    }
}
```

This table exactly matches FR-131 row-for-row (closed form): 11 explicit narrowing rows; every other primitive pair (including `Int→Long`, `UUID→String`, `Decimal→Long`, `Boolean→String`, etc.) returns `false`. AC-8.2's parameterised test pins every narrowing row; AC-8.3 pins selected widening rows produce zero diagnostics.

### 3.10 CLI `--as-of` plumbing

Two `Program.cs` sites and one `CompilerPipeline.cs` site change.

`Program.cs`. The argument parser at line 90 gains a `--as-of` case that captures the next token. After `ParseArgs` returns, `Main` resolves the date in **one** place:

```csharp
// FR-141: this is the ONLY DateTime.UtcNow read in the project, and it is in
// the CLI binary (not Gravity.Dsl.Compiler / .Ast / .Emitter). The BannedSymbols
// analyzer exempts Gravity.Dsl.Cli; the exemption is documented in
// Directory.Build.props by attaching BannedSymbolsFile only to the compiler/
// emitter projects, not the CLI.
DateOnly asOf;
if (parsed.AsOfRaw is { Length: > 0 } raw)
{
    if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
    {
        Console.Error.WriteLine(
            "gravc CLI002: --as-of value '" + raw + "' must be YYYY-MM-DD");
        return 1;
    }
}
else
{
    asOf = DateOnly.FromDateTime(DateTime.UtcNow);   // single, isolated clock read
}
```

`CompilerPipeline.Check` and `CompilerPipeline.Gen` gain a `DateOnly currentDate` parameter (with no default, so the compile breaks if anyone forgets to thread it). The pipeline passes the value directly to `Validator.Validate`. The `Phase 0–3` test harness in `Gravity.Dsl.Tests` is migrated to pass `default(DateOnly)` (== `0001-01-01`), which never trips VAL030 for any reasonable `until` date.

The `Directory.Build.props` change pulls `BannedSymbolsFile` out of the global propery group and into a `<Choose>/<When Condition="'$(MSBuildProjectName)' != 'Gravity.Dsl.Cli'">` block; this is the minimum-surface scope adjustment that lets the CLI call `DateTime.UtcNow` while the compiler and emitters remain locked down. CI runs `dotnet build -warnaserror` against both project sets, so a stray `DateTime.UtcNow` in the compiler still fails the build.

## 4. Determinism strategy

Phase 8 introduces a clock dependency that, if mis-handled, would break the project-wide byte-identical-across-runs guarantee. Five concrete commitments preserve it:

- **(a) Compiler library never reads the clock.** `Validator.Validate` takes `DateOnly currentDate` as a required parameter. `VersionDiff` consumes it; nothing in `Gravity.Dsl.Compiler/**` calls `DateTime.UtcNow`, `DateTime.Now`, or `DateTimeOffset.*`. The existing `BannedSymbolsFile` (`/workspace/gravity/BannedSymbols.txt`) already pins this; the only scope change is to exempt `Gravity.Dsl.Cli` (see §3.10).
- **(b) Diagnostic ordering per FR-160.** `VersionDiff` accumulates into a `DiagnosticSink` and applies one final sort by `(Fqn ordinal, Vnext ascending, RuleId ordinal, Span.Path ordinal, Span.Line, Span.Column)` before returning. Phase 0–3 validator diagnostics flow through unchanged; the Phase 8 block is sorted independently and appended after the Phase 0–3 block to keep golden files for existing rules stable.
- **(c) `ImmutableSortedDictionary<DeclKey, TopLevelDecl>` ordering with `DeclKeyComparer.Instance`.** Iteration produces `(Fqn ordinal asc, Version asc)` deterministically per FR-161. The C# emitter, which iterates `model.Declarations`, therefore emits files in a strictly deterministic order even when multiple in-scope versions coexist.
- **(d) `--as-of` logged for reproducibility.** When `--as-of` is **absent**, the resolved `DateOnly` is logged to `Console.Out` in verbose mode (`--verbose`, a Phase 8 add-on flag that defaults to off) as a single line `gravc: --as-of resolved to 2026-05-18`. CI runs can capture and re-supply this value; the build is then reproducible from that command-line forward.
- **(e) Banned-APIs analyzer keeps `DateTime.UtcNow` out of compiler/emitter projects.** The single allowed call site is `Gravity.Dsl.Cli/Program.cs` (one line). A CI lint rule (`grep -R "DateTime\.\(UtcNow\|Now\)" Gravity.Dsl.Compiler Gravity.Dsl.Ast Gravity.Dsl.Emitter Gravity.Dsl.Emitter.CSharp`) backs up the analyzer with a literal source-text check.

## 5. Test strategy

Three CI tiers mirroring the predecessor plan.

### 5.1 Round-trip tests

The Phase 0–3 round-trip harness (every `.gravity` under `samples/registry/` and `tests/fixtures/parser/`) is extended with two new fixtures pinning AC-8.16:

- `tests/fixtures/parser/version_qualified_basic.gravity` — properties of types `Money@2`, `Money@2?`, `Money@2[]`, `Money@2?[]`.
- `tests/fixtures/parser/version_qualified_in_command.gravity` — command arguments and return types using `@N`.

Both fixtures parse to AST `A1`, are serialized via `SourceWriter`, re-parsed to `A2`, and asserted structurally equal under `SpanIgnoringEquality.Equal(TypeRef, TypeRef)` (which gains a `na.Version == nb.Version` clause). Round-trip equivalence is what FR-100 promises authors.

### 5.2 Resolver tests

`tests/fixtures/versioning/resolver/` covers FR-120..FR-127 (AC-8.7, AC-8.8 first half, AC-8.13):

- `chain_ok_two_versions.gravity` — `Employee@1` + `Employee@2 deprecates version 1 until "2099-12-31"`: zero diagnostics; both declarations present in `Declarations` under their distinct `DeclKey`s.
- `chain_ok_three_versions.gravity` — `Employee@1`, `Employee@2 deprecates 1`, `Employee@3 deprecates 2`: zero resolver diagnostics; the diff engine sees two pairs `(1,2)` and `(2,3)`.
- `chain_missing.gravity` — two undeprecated versions of the same FQN: `RES004` on the higher-versioned decl; no breaking-change diagnostics emitted (diff engine refuses to run without a chain).
- `chain_skipped_link.gravity` — `Employee@1`, `Employee@2`, `Employee@3 deprecates 2`: `RES004` at the `(1,2)` gap and `VAL027` at the validator pass (the same fixture pins both halves of AC-8.8 in two assertions).
- `unqualified_resolves_to_max.gravity` — `Project@1`, `Project@2`, property `lead_project: Project;`: resolver's version index maps that property to `Project@2`.
- `qualified_missing_version.gravity` — property typed `Project@5` with no `Project@5` declared: `RES003` with the missing-version message naming the available versions.

### 5.3 Validator tests

`tests/fixtures/versioning/validator/` has one fixture per VAL rule sub-cause plus a combined fixture for AC-8.15:

- `val020_field_removed.gravity` — single removed property; one `VAL020`.
- `val021_narrow_*.gravity` — nine files, one per row in FR-131: `optional_lost`, `array_lost`, `decimal_to_int`, `long_to_int`, `string_to_int`, `string_to_uuid`, `datetime_to_date`, `version_decrease`, `rename` (rename emits VAL020 not VAL021; the fixture pins the suppression of VAL021).
- `val021_widen_*.gravity` — four files, one per widening row (AC-8.3): `int_to_long`, `int_to_decimal`, `long_to_decimal`, `date_to_datetime`. Zero diagnostics on each.
- `val022_state_removed.gravity`, `val022_state_added.gravity` — one fires VAL022, one is silent.
- `val023_command_removed.gravity`, `val024_event_removed.gravity` — one each.
- `val025_transition_removed.gravity` — produces `VAL025` at **warning** severity; the assertion pins severity.
- `val026_arg_removed.gravity`, `val026_arg_narrowed.gravity`, `val026_required_added.gravity`, `val026_optional_added.gravity` (last one zero diagnostics).
- `val027_skipped_link.gravity` — companion to the resolver fixture; same source different assertion.
- `val028_deprecates_missing.gravity` — `Employee@2 deprecates version 9` with no `Employee@9`.
- `val029_self_or_forward.gravity` — `Employee@2 deprecates version 2` and `Employee@2 deprecates version 3`.
- `val030_window.gravity` — paired test: `currentDate = 2026-05-18` against `until = 2026-05-17` fires `VAL030`; `currentDate = 2026-05-17` against `until = 2026-05-17` does not. Pins AC-8.10 directly.
- `combined_all_rules.gravity` — single fixture exercising VAL020..VAL030 against a single resolved model. The expected diagnostic stream is byte-checked against `tests/golden/diagnostics/phase8/combined.txt` (AC-8.15).

### 5.4 CLI integration test (AC-8.11)

`Gravity.Dsl.Tests/Cli/AsOfFlagTests.cs` invokes `CompilerPipeline.Check` and `CompilerPipeline.Gen` (in-process; the existing CLI integration tier already runs the pipeline without spawning `gravc.exe` to keep CI fast) against `tests/fixtures/versioning/cli_as_of/`:

1. With `--as-of 2099-01-01` against a fixture whose `until = "2026-12-31"` — exit 0, zero diagnostics.
2. With `--as-of 2099-01-01` against a fixture whose `until = "2098-12-31"` — exit non-zero, `VAL030` present.
3. With `--as-of 2026-13-45` — exit non-zero, `CLI002` present.
4. With no `--as-of` and a fixture whose `until = "9999-12-31"` — exit 0 (sanity).

### 5.5 AST stability regression (AC-8.14)

A new test in `Gravity.Dsl.Tests/AstStabilityTests.cs` loads the previously-published `Gravity.Dsl.Ast` 1.0.0 assembly (vendored in `tests/vendor/Gravity.Dsl.Ast.1.0.0/`) into an isolated `AssemblyLoadContext` and asserts that a stub emitter compiled against `1.0.0` loads against the live `1.1.0` compiler host without binding errors. The test also asserts `AstVersion.Value == "1.1.0"` literally.

## 6. Risk register (Phase 8 surface)

| Risk | Surface in Phase 8 | Mitigation |
|---|---|---|
| `ResolvedModel.Declarations` decl-key change breaks Phase 0–3 callers. | `model.Declarations`'s key type goes from `string` to `DeclKey`. Anything depending on the string-keyed shape would fail to compile, but pattern-matching/positional callers could silently misread. | Comprehensive grep on `model.Declarations`, `\.Declarations\b`, and `ImmutableSortedDictionary<string,\s*TopLevelDecl>` before P8b begins; every site listed in §3.2 is touched in the same PR; full test suite must be green before P8c starts. |
| Narrowing table is judgment-laden (`String → any non-String`). | The "non-String primitive narrows from String" call is a contract choice, not a derivation. Wrong table = wrong diagnostics. | Lock the table in spec FR-131; AC-8.2 has one fixture per row; AC-8.3 has one fixture per widening row; any future change to the table requires a spec amendment and a golden-file update. |
| Deprecation-window check is sensitive to time zones. | A naïve `DateTime.UtcNow.Date` vs `DateTime.Parse(until).Date` mismatch in any non-UTC zone would produce off-by-one diagnostics. | Pure `DateOnly` arithmetic throughout: no `DateTime`, no `TimeSpan`, no `TimeZoneInfo`. The single UTC clock read at `Program.cs` converts via `DateOnly.FromDateTime(DateTime.UtcNow)` and never touches time-of-day. |
| `@N` parser changes adjacent to property/relation parsing. | Inserting `@N` recognition between identifier and `?`/`[]` could regress the existing modifier interaction. | Parser unit tests per AC-8.12 cover positive (`Money@2`, `Money@2?`, `Money@2[]`, `Money@2?[]`) and negative (`Money@`, `Money@01`, `Money@-1`, `Money@ 2`, `Int@2`, `Employee@2` on a relation target) cases; the existing FR-011 order tests stay green. |
| Positional record change on `NamedTypeRef` risks pattern-match breakage. | Inserting `int? Version` between `Name` and `IsOptional` invalidates any positional deconstruction. | AC-8.14 regression test plus a project-wide `ast-grep` for `is NamedTypeRef\s*\(` (positional) before merge; current grep finds zero such patterns (§3.3). Migrate any introduced positional pattern to named-property syntax. |
| False positives on `VAL021` from generated round-trip diff order. | If `DiffByName` were to compare fields by index instead of by name, reordering would falsely fire VAL021. | The diff engine is name-keyed, not index-keyed (§3.8). Field reordering is silently allowed; only same-named fields contribute to VAL021. Pinned by a `val021_reordered_no_diagnostic.gravity` fixture in §5.3. |
| Skipped-link chain detection (`VAL027`) is subtle. | The resolver fires `RES004` on the gap and the validator fires `VAL027` on the broken chain; getting the interaction wrong double-counts or misses. | AC-8.8 has two assertions on the same fixture: one for the resolver-layer `RES004` count and one for the validator-layer `VAL027` count. Spec FR-137 is unambiguous on the predicate: `VAL027` fires when an intermediate version coexists but is **not named** by its successor's deprecates clause. |
| CLI clock read is the only un-banned API call site. | A future refactor might "tidy up" the date-resolution helper into the compiler library; the analyzer would then start failing every build. | Clock read isolated to `Program.cs` `Main`; documented inline as the only `DateTime.UtcNow` call site (comment block above the call, copied verbatim from FR-141). The `BannedSymbolsFile` is conditionally attached to compiler/emitter projects via `<Choose>` in `Directory.Build.props` so the analyzer remains active everywhere except the CLI. |

## 7. Out-of-scope acknowledgements

Documented here so they do not creep in. Each item maps to a spec non-goal (NG-1..NG-7) or is carried over from the predecessor plan.

- **Cross-compile diffing via a persisted manifest of "what was emitted last time"** (NG-1). Phase 8 diffs only versions that coexist in the current input set. The on-disk diff store, manifest format, and `gravc diff` subcommand are deferred to Phase 9+.
- **Per-field `@deprecated` annotations** (NG-2). The whole-entity `deprecates` clause is the only deprecation surface in Phase 8. The VAL020 message body explicitly directs authors to "mark `?`, keep the field, or wait for the per-field annotation in Phase 9+", which is the only hint Phase 8 carries about the future surface.
- **Runtime guards** (NG-3). Phase 8 is compile-time only. The C# emitter does not emit any "this field was removed in @N" runtime check; if and when one is needed it lives in Phase 9+ and is governed by a separate spec.
- **Semantic-default-change detection** (NG-4). The v1 grammar has no field defaults, so this Principle IV taxonomy bullet has nothing to detect. When defaults land they will arrive with their own VAL rule.
- **Auto-migration tooling** (NG-5). No automated rewrite of v1 call sites to `Foo@1`. Authors who do not want max-version semantics qualify the type ref by hand.
- **Phases 4–7 emitters, MSBuild integration, LSP, formatter, Registry features, AI authoring tooling** (NG-6). All Phase 0–3 out-of-scope discipline carries forward.
- **Cross-namespace diffing edge cases where the same simple name appears under different FQNs** (NG-7). Phase 8 diffs strictly within a single FQN; two entities sharing a simple name under different namespaces are not version-pair candidates.
- **All Registry concerns per Principle VII**: scopes, permissions, rules, releases, library imports. Not addressed in any phase of this plan.
