# Gravity DSL — Emitter Authoring Guide

Status: Phase 9b deliverable (NG-1). Updated 2026-05-18.

This guide tells you how to ship a Gravity DSL emitter for a target the
project does not yet support — Kotlin, Python, Rust, a bespoke JSON Schema
dialect, an OpenAPI variant, your company's internal RPC IDL, whatever
you need. Everything in here is grounded in the C# reference emitter at
`Gravity.Dsl.Emitter.CSharp/CSharpEmitter.cs` and the minimal Outline
sample at `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/OutlineEmitter.cs`.

---

## 1. Audience and prerequisites

This guide is for a .NET developer who wants to author a Gravity emitter
plugin. You are not extending the compiler. You are not editing the AST.
You are writing a class that implements `IEmitter`, packaging it as a
NuGet, and dropping it next to the host so the CLI or MSBuild target
loads it.

### What you should already know

- C# and .NET 9 fundamentals (records, `ImmutableSortedDictionary<,>`,
  `init`-only properties).
- The NuGet packaging model: `IsPackable`, `PackageId`, `lib/net9.0/`,
  `buildTransitive/`.
- Enough compiler / AST literacy to read a sealed `record` graph and
  walk it without surprise. The Gravity AST is small (~20 records) and
  exhaustively documented; see `Gravity.Dsl.Ast/README.md`.

You do **not** need to know parser internals, the resolver, the
validator, or the versioning algorithm. You consume the *resolved*
model, which is already correct by construction.

### Repository orientation

| Path | What lives there |
| --- | --- |
| `Gravity.Dsl.Ast/` | The read-only AST records. Your emitter walks these. |
| `Gravity.Dsl.Emitter/` | `IEmitter`, host, registry, output sink, config loader. |
| `Gravity.Dsl.Emitter.CSharp/` | Reference C# emitter. The production-grade example. |
| `samples/emitters/outline/` | Minimal sample emitter. The copy-paste template. |
| `tests/golden/csharp/` | Locked golden tree for the C# emitter. |
| `tests/fixtures/parser/` | Sample `.gravity` inputs you can borrow. |
| `specs/001-gravity-dsl/`, `specs/002-phase-8-versioning/`, `specs/003-phase-9-build-integration/` | Phase specs (FR-… cited throughout). |
| `CLAUDE.md` | Project constitution. Principles I and VI govern the emitter contract. |

### Where to ask questions

- Repository issues: file a question with the `emitter-authoring` label
  and reference the FR-… number you are stuck on.
- For SDK-level questions about emitting C# / GraphQL / OpenAPI / etc.,
  prefer the upstream docs over guesses. The Gravity AST gives you the
  domain; you choose the target idiom.

---

## 2. The IEmitter contract

`IEmitter` is the entire contract. Five members. No base class. No
lifecycle hooks beyond a single `Emit` call per run. The contract lives
at `Gravity.Dsl.Emitter/IEmitter.cs`.

### 2.1 Members

| Member | Purpose |
| --- | --- |
| `string TargetName` | Stable id used by the Gravity config file (`.gravity.yaml`; legacy `.gravity.config`) to address this emitter, and by diagnostics as the origin path. Example: `"csharp"`. |
| `string AnnotationNamespace` | Annotation namespace this emitter claims, e.g. `"csharp"`. Empty string means "claim nothing". See §5. |
| `SemanticVersionRange SupportedAstVersions` | AST contract range you compiled against. The host rejects an incompatible emitter with `HOST001`. See §3. |
| `EmitterConfigSchema ConfigurationSchema` | Declarative schema for your `.gravity.yaml` block (legacy `.gravity.config` also accepted). The host validates user input against this before invoking you. See §10. |
| `EmitResult Emit(model, config, sink)` | The only entry point. Write files into `sink`, return diagnostics. |

### 2.2 Bare-minimum implementation

The Outline sample is the canonical template. Verbatim from
`samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/OutlineEmitter.cs:22-40`:

```csharp
public sealed class OutlineEmitter : IEmitter
{
    public const string ConfigKeyOutput = "output";

    public string TargetName => "outline";

    public string AnnotationNamespace => "outline";

    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");

    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput, ConfigValueKind.String, Required: true, Default: null)
    ));
    // ... Emit body ...
}
```

Two rules to internalise from this shape:

1. **`sealed class` with a public parameterless constructor.** The
   registry instantiates emitters reflectively
   (`EmitterRegistry.cs:114-119`). No DI container is involved; pass
   state through `EmitterConfig` only.
2. **All five members are surfaceable on the type itself.** No
   inheritance, no marker attributes, no fluent builder. The DLL drops
   in, the host finds the type, calls the property getters.

### 2.3 Emit body

The `Emit` method runs once per build, on one thread per emitter (the
host parallelises *across* emitters; a single emitter is sequential).
Required pattern, from `OutlineEmitter.cs:43-93`:

```csharp
public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
{
    if (model is null) throw new ArgumentNullException(nameof(model));
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (sink is null) throw new ArgumentNullException(nameof(sink));

    var typedConfig = OutlineEmitterConfig.From(config);
    var declToFile = BuildDeclToFile(model);

    foreach (var kv in model.Declarations)
    {
        var decl = kv.Value;
        if (!declToFile.TryGetValue(kv.Key.Fqn, out var sourceFile)) continue;
        string? dslNs = sourceFile.Namespace?.Name;
        string dir = Combine(typedConfig.Output, ComposeDirectory(dslNs));

        switch (decl)
        {
            case EntityDecl entity:
                sink.WriteFile(Combine(dir, entity.Name + ".md"),
                    EntityOutlineRenderer.Render(entity, kv.Key.Version));
                break;
            case ValueTypeDecl vt:
                sink.WriteFile(Combine(dir, vt.Name + ".md"),
                    ValueTypeOutlineRenderer.Render(vt, kv.Key.Version));
                break;
            case EnumDecl en:
                sink.WriteFile(Combine(dir, en.Name + ".md"),
                    EnumOutlineRenderer.Render(en, kv.Key.Version));
                break;
        }
    }

    return new EmitResult(ImmutableArray<Diagnostic>.Empty);
}
```

Three invariants every `Emit` must hold:

- Iterate `model.Declarations` directly (it is already sorted; see §6).
- Do not throw for content-level errors — return them in
  `EmitResult.Diagnostics` so the host can sort and aggregate. Throwing
  surfaces as `HOST004` (`EmitterHost.cs:152-156`).
- Never write to disk directly. Always go through `sink.WriteFile`.

---

## 3. AST version pinning

### 3.1 Why versioned

Per the constitution (`CLAUDE.md`, "Architectural constraints — Stable
AST contract"), the compiler publishes a versioned AST through a public
NuGet contract. Third-party emitters lock against a range of that
contract. The AST version is independent of the DSL grammar version: an
additive grammar change does not bump the AST when no public record
changes shape.

This is the mechanism that lets the AST evolve without breaking your
emitter.

### 3.2 Declaring `SupportedAstVersions`

The property is a `SemanticVersionRange` parsed from a space-separated
AND-composition (`Gravity.Dsl.Emitter/SemanticVersionRange.cs:18`).
Supported operators: `>=`, `>`, `<=`, `<`, `=` (or bare version meaning
`=`). Example:

```csharp
public SemanticVersionRange SupportedAstVersions { get; } =
    SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
```

Convention: target one major. If you depend on a feature added in 1.1.0,
say `">=1.1.0 <2.0.0"`. Do not pin a single patch; that just makes the
emitter fragile.

### 3.3 What happens on incompatibility

`EmitterRegistry.Build` compares each emitter's range against the
current `AstVersion.Value`. From `EmitterRegistry.cs:135-157`:

```csharp
foreach (var e in emitters)
{
    bool ok = e.SupportedAstVersions.Satisfies(AstVersion.Value);
    if (!ok)
    {
        diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            RuleIds.Host001,
            "emitter '" + e.TargetName + "' declares SupportedAstVersions='"
                + e.SupportedAstVersions + "' which excludes AstVersion '" + AstVersion.Value + "'",
            new SourceSpan(e.TargetName, 1, 1, 0)));
        continue;
    }
    compatible.Add(e);
}
```

A rejected emitter is dropped from the registry. `HOST001` is propagated
through the host's diagnostics array and surfaces to the CLI.

### 3.4 AstVersion history

| AstVersion | Phases | Change |
| --- | --- | --- |
| `1.0.0` | Phases 0–3 | Initial public AST. `EntityDecl`, `ValueTypeDecl`, `EnumDecl`, all field / event / command / annotation records. |
| `1.1.0` | Phase 8 | `NamedTypeRef` gains optional `int? Version` (appended as the last positional parameter with default `null`). Source built against 1.0.0 continues to compile and run unchanged. See `Gravity.Dsl.Ast/Types/NamedTypeRef.cs` and `Gravity.Dsl.Ast/README.md` §1.1.0. |

The current value lives at `Gravity.Dsl.Ast/AstVersion.cs:15`:

```csharp
public const string Value = "1.1.0";
```

### 3.5 Bumping your range when the AST moves

Rules per `Gravity.Dsl.Ast/README.md` §"Additive-only versioning":

- A new optional field with a backward-compatible default ships in a
  **minor** AST bump. You **do not need to bump your range** for this —
  `">=1.0.0 <2.0.0"` accepts every 1.x.
- A new record type ships in a minor bump. Same: no action needed.
- A breaking change (field removal, type narrowing, shape change) is a
  **major** bump and requires a documented migration path + deprecation
  window. When that happens, you publish a new emitter version with the
  updated range (e.g. `">=2.0.0 <3.0.0"`) and continue maintaining the
  1.x line until the deprecation window closes.

Practical advice: if your code reads `NamedTypeRef.Version`, you depend
on 1.1.0. Set the range floor accordingly.

---

## 4. Plugin discovery

### 4.1 How the host finds you

The CLI calls `EmitterRegistry.Discover(pluginDirectory)`
(`EmitterRegistry.cs:67-94`). It scans the directory for `*.dll`, loads
each into an isolated `AssemblyLoadContext`, walks public types
implementing `IEmitter` with a public parameterless ctor, and
instantiates them.

Behaviour worth knowing:

- DLLs that fail to load are silently skipped — not every DLL in a
  plugin directory is meant to be an emitter (transitive dependencies
  live there too).
- Types are sorted by `FullName` ordinal before instantiation, so
  discovery order is deterministic.
- A type that throws from its constructor is silently dropped.
- After discovery, emitters are sorted by `TargetName` ordinal
  (`EmitterRegistry.cs:160`).

For in-process tests you skip discovery entirely with
`EmitterRegistry.FromInstances(...)` (`EmitterRegistry.cs:55-59`). The
same compatibility and ownership checks run.

### 4.2 Where to place the DLL

There are two surfaces a real emitter has to support:

**The CLI plugin directory.** Drop
`Your.Emitter.dll` into the directory the CLI's `--plugins` flag points
at. That is the path that `Discover` scans.

**The MSBuild integration.** This is the Phase 9 cross-package wiring.
Your NuGet package must:

1. Ship `lib/net9.0/Your.Emitter.dll` (standard NuGet layout —
   `IsPackable=true` does this for you when the assembly is
   `net9.0`-targeted).
2. Ship a `buildTransitive/Your.Emitter.props` file that contributes
   the assembly path to MSBuild's `<GravityDslEmitterAssembly>` item
   group.

The Outline sample's props
(`samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/buildTransitive/Gravity.Dsl.Emitter.Sample.Outline.props`)
is the canonical example:

```xml
<Project>
  <ItemGroup>
    <GravityDslEmitterAssembly
      Include="$(MSBuildThisFileDirectory)..\lib\net9.0\Gravity.Dsl.Emitter.Sample.Outline.dll" />
  </ItemGroup>
</Project>
```

The csproj wires it into the package
(`samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Gravity.Dsl.Emitter.Sample.Outline.csproj:43-45`):

```xml
<None Include="buildTransitive\Gravity.Dsl.Emitter.Sample.Outline.props"
      Pack="true"
      PackagePath="buildTransitive\Gravity.Dsl.Emitter.Sample.Outline.props" />
```

NuGet auto-imports `buildTransitive/<package-id>.props` at the top of
the consuming `.csproj`. Once your package is referenced alongside
`Gravity.Dsl.MsBuild`, MSBuild's `GravityDslGenerate` target
(`Gravity.Dsl.MsBuild/buildTransitive/...targets`) passes every
`<GravityDslEmitterAssembly>` item to the task, which loads them into
the registry alongside the built-in C# emitter.

The file name must match the package id (`Your.Emitter.props` for
package id `Your.Emitter`) for NuGet to auto-import it.

### 4.3 Annotation namespace ownership collisions

`EmitterRegistry.Build` groups discovered emitters by
`AnnotationNamespace` and emits `HOST002` for any non-empty namespace
claimed by two or more emitters
(`EmitterRegistry.cs:164-197`). One diagnostic per unordered pair, so a
three-way collision surfaces all three pairings.

See §5 for how to pick a namespace.

---

## 5. Annotation namespace ownership

### 5.1 Why namespaces are unique per emitter

Annotations are how a Gravity author sends target-specific hints
through the DSL without polluting the core grammar (Principle VI:
"Target-specific hints use namespaced annotations whose ownership is
registered by the consuming emitter").

The annotation in source looks like this:

```
entity Customer version 1 {
  identity id: UUID;
  @csharp(serializable: true)
  properties {
    name: String;
  }
}
```

`csharp(...)` is the annotation namespace `csharp` invoking the
hint `serializable`. The `csharp` emitter owns the `csharp` namespace
and is the only emitter authorised to read those hints. The validator
emits `VAL006` on any annotation whose namespace nobody claims —
that is wired through
`EmitterRegistry.ClaimedAnnotationNamespaces()` (`EmitterRegistry.cs:37-48`).

### 5.2 How to claim one

Set `AnnotationNamespace` to the string you want to own.

```csharp
public string AnnotationNamespace => "kotlin";
```

Rules:

- Must be a single identifier or a dotted path. The validator treats it
  ordinally — no normalisation.
- Must be unique across the registered emitter set. Two emitters
  claiming the same non-empty namespace produce `HOST002`.
- Empty string opts out (`"claim nothing"`). Useful for diagnostic-only
  or no-op stub emitters.

### 5.3 Examples

| Emitter | TargetName | AnnotationNamespace |
| --- | --- | --- |
| Reference C# (`CSharpEmitter.cs:26-29`) | `csharp` | `csharp` |
| Outline sample (`OutlineEmitter.cs:28-31`) | `outline` | `outline` |
| Future JSON Schema | `json-schema` | `json-schema` |
| Future GraphQL | `graphql` | `graphql` |

By convention, `TargetName` and `AnnotationNamespace` match. They are
distinct concepts (a target identifier vs. a hint vocabulary) but
keeping them aligned is the principle of least surprise.

### 5.4 How emitters consume their own annotations

The annotations attached to a declaration live on the `Annotations`
property — e.g. `EntityDecl.Annotations` is an
`ImmutableArray<AnnotationDecl>` (`Gravity.Dsl.Ast/Declarations/EntityDecl.cs`).
Filter to your namespace by name and read the typed value. The C#
emitter does exactly this in `Renderers.cs` when rendering
`@csharp(...)` hints. The validator has already pre-checked the
namespace, so a `null`-safe filter is enough.

```csharp
foreach (var anno in entity.Annotations)
{
    if (!string.Equals(anno.Namespace, AnnotationNamespace, StringComparison.Ordinal))
        continue;
    // anno.Name + anno.Arguments — emitter-specific interpretation
}
```

`AnnotationDecl.Arguments` is an `ImmutableSortedDictionary` (not
`ImmutableDictionary`) so iteration order is byte-stable —
`Gravity.Dsl.Ast/README.md` §"Read-only contract" makes this a contract
guarantee.

---

## 6. The Resolved AST: walking the model

### 6.1 What's in ResolvedModel

`Gravity.Dsl.Compiler/Resolution/ResolvedModel.cs:15-27`:

```csharp
public sealed record ResolvedModel(
    ImmutableSortedDictionary<DeclKey, TopLevelDecl> Declarations,
    IReadOnlyList<SourceFile> Files,
    IReadOnlyDictionary<string, ImmutableSortedDictionary<string, TopLevelDecl>> FileImports)
{
    public ImmutableSortedDictionary<string, ImmutableArray<int>> VersionIndex { get; init; }
        = ImmutableSortedDictionary<string, ImmutableArray<int>>.Empty.WithComparers(System.StringComparer.Ordinal);
}
```

| Member | Use |
| --- | --- |
| `Declarations` | The primary surface. Sorted by `(Fqn ordinal asc, Version asc)`. Iterate this; do not re-sort. |
| `Files` | Original `SourceFile` list, preserves source order. Useful for emitters that emit one output per input file or want to recover the original `.gravity` path for a header. |
| `FileImports` | Per-file import scope. Resolves an unqualified `Foo` in source `a.gravity` to the right declaration. Most emitters ignore it because `Declarations` is already keyed by FQN. |
| `VersionIndex` | FQN → ascending list of declared versions. Phase 8 surface. Use this if you emit per-version artifacts. |

### 6.2 Iterating entities / value types / enums

The model holds three concrete `TopLevelDecl` subtypes:
`EntityDecl`, `ValueTypeDecl`, `EnumDecl` (and their fields, events,
commands, lifecycle, etc.). Pattern-match in the iteration loop. Verbatim
from `CSharpEmitter.cs:57-93`:

```csharp
foreach (var kv in model.Declarations)
{
    var decl = kv.Value;
    // ...
    switch (decl)
    {
        case ValueTypeDecl vt: /* emit */ break;
        case EnumDecl en:      /* emit */ break;
        case EntityDecl entity: /* emit record + state enum + events + commands */ break;
    }
}
```

Iteration order is the FR-161 contract — `(Fqn ordinal, Version asc)`,
locked by `DeclKeyComparer` (`Gravity.Dsl.Compiler/Versioning/DeclKey.cs:30-35`).
You can rely on it; it is part of the public surface.

### 6.3 Resolving `NamedTypeRef` (including `@N` version qualifiers)

A `NamedTypeRef` is what appears in property and field types when the
author writes a user-declared type by name. The AST shape
(`Gravity.Dsl.Ast/Types/NamedTypeRef.cs:16-22`):

```csharp
public sealed record NamedTypeRef(
    string Name,
    bool IsOptional,
    bool IsArray,
    SourceSpan Span,
    int? Version = null)
    : TypeRef(Span);
```

- `Name` is the as-written identifier (often a simple name like
  `ContactInfo`; sometimes a dotted path).
- `IsOptional` / `IsArray` are the `?` and `[]` modifiers.
- `Version` is the optional `@N` qualifier from source (e.g.
  `ContactInfo@2`). `null` means "no `@N` was written; bind to the
  maximum declared version in scope" — the resolver did that
  binding already if you need it.

To resolve a `NamedTypeRef` to a concrete declaration:

1. Compute the FQN. If `Name` is a dotted path, that is the FQN. If it
   is a simple name, look it up in `FileImports` for the source file
   that owns the referring declaration — that returns the bound
   `TopLevelDecl`. Most emitters do the simple-name lookup once at the
   start of `Emit` and cache the FQN per declaration.
2. Resolve the version. If `Version` is non-null, you key
   `Declarations` with `new DeclKey(fqn, version)`. If it is null,
   `VersionIndex[fqn]` is non-empty (the resolver populates it) and the
   last entry is the maximum version.

### 6.4 Phase 8 versioning awareness

Multiple versions of the same FQN can coexist in `Declarations`. They
appear as separate `DeclKey` entries. If your target idiom does not
support coexistence (e.g. a single language type per name), pick the
maximum version per FQN and skip earlier ones. If it does, emit each
version into a versioned subdirectory or with a versioned suffix.

You can compute the "latest per FQN" filter in one pass:

```csharp
foreach (var (fqn, versions) in model.VersionIndex)
{
    int latest = versions[versions.Length - 1];
    var decl = model.Declarations[new DeclKey(fqn, latest)];
    // emit `decl` only
}
```

---

## 7. Writing output: IEmitterOutput

### 7.1 The sink contract

`Gravity.Dsl.Emitter/IEmitterOutput.cs:10-17`:

```csharp
public interface IEmitterOutput
{
    void WriteFile(string relativePath, string contents);
}
```

`relativePath` is resolved against your emitter's configured `output`
root. `contents` is the file body as a single string.

### 7.2 Why writes are buffered

The host hands you a `BufferedEmitterOutput`
(`Gravity.Dsl.Emitter/BufferedEmitterOutput.cs`). Calls to `WriteFile`
populate an in-memory dictionary keyed by relative path. After your
`Emit` returns, the host:

1. Sorts the buffer keys under `StringComparer.Ordinal`.
2. Writes them one at a time, UTF-8 (no BOM), LF line endings only.
3. Creates parent directories as needed.

That is the FR-083 / AC-6a contract. Emitter authoring style cannot
break determinism because the order in which you call `WriteFile` does
not affect on-disk creation order.

### 7.3 Output path conventions

- Always relative. The buffer normalises `\` to `/` on entry
  (`BufferedEmitterOutput.cs:114-119`), but never let a backslash into
  your key in the first place — it keeps your debug prints clean on
  every platform.
- Use namespace-derived subdirectories where the target idiom expects
  them (C# does: one directory per dotted-namespace segment via
  `NamespaceMapper.ComposeDirectory`). The Outline sample mirrors the
  same pattern (`OutlineEmitter.cs:113-119`).
- Do not include the configured `output` root in the relative path you
  pass to `WriteFile` — the host prepends it during the commit phase.
  The exception is the Outline sample, which includes `output` in the
  key because it carries the typed config through to the path
  construction step. Both work; pick one and be consistent.

### 7.4 Path safety rules

`BufferedEmitterOutput.WriteFile` rejects two patterns up-front
(`BufferedEmitterOutput.cs:30-44`):

- Rooted paths (`/etc/passwd`, `C:\Windows\...`) — throws
  `ArgumentException`.
- Any segment equal to `..` — throws `ArgumentException`.

At commit time it canonicalises the full path and refuses to write
anything that resolves outside the configured output root
(`BufferedEmitterOutput.cs:79-90`). This is defence-in-depth; the host
also runs a pre-flight check on the configured `output` value itself
(`EmitterHost.cs:71-98`, `CFG004`).

Practical implication: if you compute a relative path with
`Path.Combine`, it is fine. If you let user data flow into the path
unvalidated, you get an `ArgumentException` at write time, not a
disk-write outside the project tree.

### 7.5 C# reference snippet

`CSharpEmitter.cs:98-103`:

```csharp
private static void EmitOne(IEmitterOutput sink, string relPath, string sourceGravityRelPath, string body)
{
    var formatted = CSharpFileFormatter.Format(body, sourceGravityRelPath);
    sink.WriteFile(relPath.Replace('\\', '/'), formatted);
}
```

The explicit `Replace('\\', '/')` is belt-and-braces: the buffer
normalises anyway, but stripping the backslash before the buffer call
means a debug print of `relPath` is also clean.

---

## 8. Determinism — non-negotiable

### 8.1 Why

Constitution Principle I — "The DSL is the spec" — depends on
deterministic output. Generated artifacts are checked into source
control or committed-from-source on every build. A non-deterministic
byte in a generated file produces a noisy diff every CI run and a
"who changed that file" question with no answer. Two emitter runs
against the same model **must** produce byte-identical bytes.

`Gravity.Dsl.Tests/Emitter/CSharp/DeterminismTests.cs` enforces this
on the C# reference emitter. Your emitter must pass an analogous test
(§9).

### 8.2 Banned APIs

`Gravity.Dsl.Tests/...` is exempt; everything else under the project,
including your emitter, must compile with
`Microsoft.CodeAnalysis.BannedApiAnalyzers` active and `BannedSymbols.txt`
attached.

Verbatim from `/workspace/gravity/BannedSymbols.txt`:

```
P:System.DateTime.Now; non-deterministic clock; emitter output must be deterministic
P:System.DateTime.UtcNow; non-deterministic clock; emitter output must be deterministic
P:System.DateTimeOffset.Now; non-deterministic clock; emitter output must be deterministic
P:System.DateTimeOffset.UtcNow; non-deterministic clock; emitter output must be deterministic
M:System.Guid.NewGuid; non-deterministic identifier; emitter output must be deterministic
P:System.Environment.MachineName; environment-dependent value; emitter output must be deterministic
P:System.Environment.UserName; environment-dependent value; emitter output must be deterministic
M:System.IO.Path.GetTempFileName; environment-dependent value; emitter output must be deterministic
M:System.IO.Path.GetTempPath; environment-dependent value; emitter output must be deterministic
T:System.Random; non-deterministic randomness; emitter output must be deterministic
M:System.String.ToUpper; culture-sensitive; use ToUpperInvariant
M:System.String.ToLower; culture-sensitive; use ToLowerInvariant
M:System.String.Compare(System.String,System.String); culture-sensitive; pass a StringComparison
```

### 8.3 Enabling the analyzer

Copy the wiring from `Directory.Build.props`. Your emitter's csproj
inherits it automatically when placed under the repo root. If your
emitter lives in its own repo, add this to its csproj:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers"
                    PrivateAssets="all"
                    IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  <AdditionalFiles Include="BannedSymbols.txt" />
</ItemGroup>
```

Copy `BannedSymbols.txt` from the Gravity repo into your project, or
reference it transitively. The list is short and stable.

### 8.4 Sorted enumerations

Use `StringComparer.Ordinal` everywhere a string key is ordered:

```csharp
var map = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
```

For sorted collections, use `ImmutableSortedDictionary` (not
`ImmutableDictionary`). The AST already uses
`ImmutableSortedDictionary` for annotation arguments, which is part of
its contract (`Gravity.Dsl.Ast/README.md` §"Read-only contract"). When
you build your own intermediate maps, follow the same rule.

### 8.5 Line endings

LF only. Never `Environment.NewLine`. Never `\r\n`. The
`BufferedEmitterOutput.CommitTo` writer uses `NewLine = "\n"` explicitly
(`BufferedEmitterOutput.cs:110`), so the file on disk is LF. But the
string you hand to `WriteFile` must also be LF — if you concatenate
`\r\n` into the contents string, that goes to disk verbatim, breaks
cross-platform byte comparison, and the determinism test fails.

A `StringBuilder` with manual `"\n"` is fine. A
`writer.WriteLine(...)` on a `StringWriter` is **not** unless you have
overridden `NewLine` on it.

### 8.6 Cultures

Every numeric or date-like `ToString` call passes
`CultureInfo.InvariantCulture`. The C# emitter does this in two places
(`Renderers.cs:300-301`):

```csharp
AnnotationIntValue i => i.Value.ToString(CultureInfo.InvariantCulture),
AnnotationDecimalValue d => d.Value.ToString(CultureInfo.InvariantCulture),
```

The C# file formatter goes one step further and pins the thread
culture for the duration of the run
(`Gravity.Dsl.Emitter.CSharp/CSharpFileFormatter.cs:32`). If you are
calling into Roslyn or any other formatter that respects
`Thread.CurrentThread.CurrentCulture`, do the same.

### 8.7 Stable iteration order for hash maps

`Dictionary<TKey, TValue>` iteration order is **not specified**.
`ImmutableDictionary<TKey, TValue>` is implementation-defined
(currently a hash trie, so iteration order is value-dependent).

Rules of thumb:

- For lookup-only `Dictionary` use, fine.
- The moment you iterate to emit output, replace with
  `ImmutableSortedDictionary<TKey, TValue>` (ordinal comparer for
  string keys).

### 8.8 Cross-platform CI

The reference emitter's CI runs the byte-compare on Linux and macOS.
You should do the same. Catches LF vs CRLF, locale-driven `ToString`,
and path-separator drift.

A minimal matrix in GitHub Actions:

```yaml
strategy:
  matrix:
    os: [ubuntu-latest, macos-latest]
steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with: { dotnet-version: '9.0.x' }
  - run: dotnet build -c Release
  - run: dotnet test -c Release
```

---

## 9. Golden-file testing

### 9.1 Why goldens

Goldens are the right way to lock emitter output. Behaviour-level tests
catch obvious regressions; goldens catch the byte-level ones — the
extra trailing newline, the moved comment, the doc-comment wording
change you did not intend to ship. Every deliberate change to emitter
output requires reviewing a golden diff.

This is the AC-2 contract for the C# reference emitter
(`Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs:14-18`).

### 9.2 Harness shape

Borrow the C# emitter's pattern. Two tests:

1. **`EveryGoldenFile_IsProducedByTheEmitter_AndMatchesByteForByte`** —
   every file in `tests/golden/<target>/` is produced by the emitter
   and matches byte-for-byte.
2. **`EmitterProducesNoExtraFiles_BeyondTheGoldens`** — the emitter
   does not produce files outside the locked set.

Verbatim from `GoldenFileTests.cs:42-76`:

```csharp
[Fact]
public async Task EveryGoldenFile_IsProducedByTheEmitter_AndMatchesByteForByte()
{
    var goldenRoot = SamplesLoader.GoldenCSharpDir();
    var goldens = Directory.GetFiles(goldenRoot, "*.cs", SearchOption.AllDirectories);
    goldens.Should().NotBeEmpty(because: "tests/golden/csharp must be populated");

    var emitted = await RunCSharpEmitter();

    foreach (var goldenPath in goldens)
    {
        var rel = Path.GetRelativePath(goldenRoot, goldenPath).Replace('\\', '/');
        emitted.Keys.Should().Contain(rel);
        var goldenText = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
        emitted[rel].Should().Be(goldenText);
    }
}

[Fact]
public async Task EmitterProducesNoExtraFiles_BeyondTheGoldens()
{
    var goldenRoot = SamplesLoader.GoldenCSharpDir();
    var goldens = /* read filenames */;
    var emitted = await RunCSharpEmitter();
    var extras = emitted.Keys.Where(k => !goldens.Contains(k)).ToArray();
    extras.Should().BeEmpty();
}
```

The `Replace("\r\n", "\n")` in the golden read is the trick that makes
the test pass when Windows checkouts add CR before the LF in the
checked-in golden file — your in-memory bytes are LF, the on-disk byte
sometimes is not, and we want to compare semantically equal contents,
not line-ending wars.

### 9.3 `UPDATE_GOLDEN=1` convention

Across the project, when an emitter change is deliberate the developer
runs:

```bash
UPDATE_GOLDEN=1 dotnet test --filter GoldenFileTests
```

…and the harness writes new bytes to the golden tree. The maintainer
reviews the diff in the PR as if it were hand-edited code, and merges
once it looks right. Implement the same env-var hook in your test:

```csharp
if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
{
    foreach (var kv in emitted)
    {
        var fullPath = Path.Combine(goldenRoot, kv.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, kv.Value);
    }
    return;
}
```

### 9.4 Where to put golden files

| Path | Contents |
| --- | --- |
| `tests/fixtures/<scenario>/` | Input `.gravity` sources, shared across emitters where possible. |
| `tests/golden/<target>/` | Locked emitter output, one directory tree per target. |

For your own emitter:
`tests/golden/kotlin/` (or whatever your `TargetName` is). Mirror the
shape of `tests/golden/csharp/` — committed, sorted, no `.DS_Store`,
no IDE droppings.

### 9.5 Handling deliberate output changes

Process:

1. Make the emitter change.
2. Run `dotnet test` — golden tests fail.
3. Run `UPDATE_GOLDEN=1 dotnet test` — goldens rewritten.
4. `git diff tests/golden/<target>/` — review like any other code.
5. Commit the emitter change and the golden update together.

The golden diff is the most important part of the review. Treat
unexpected lines, moved blocks, or whitespace deltas as bugs, not
noise.

---

## 10. Configuration schema

### 10.1 ConfigurationSchema declaration

A schema is an `ImmutableArray<ConfigKey>` exposed through your
emitter's `ConfigurationSchema` property. The C# reference shape
(`CSharpEmitter.cs:36-39`):

```csharp
public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
    new ConfigKey(ConfigKeyNamespace, ConfigValueKind.String, Required: false, Default: null),
    new ConfigKey(ConfigKeyFileScopedNamespaces, ConfigValueKind.Bool, Required: false, Default: true)
));
```

A `ConfigKey` is four fields (`EmitterConfigSchema.cs:28-32`):

| Field | Type | Use |
| --- | --- | --- |
| `Name` | `string` | The YAML key. |
| `Kind` | `ConfigValueKind` (`String`, `Int`, `Bool`) | Scalar kind. Mirrors YAML's scalar shapes. |
| `Required` | `bool` | When `true`, the loader emits `CFG003` if the key is absent. |
| `Default` | `object?` | Default applied when the key is absent and not required. |

Two implicit keys are handled by the host itself and must not appear in
your schema:

- `enabled` — `bool`, default `true`. The host honours this to skip
  disabled emitters in `EmitterHost.Run` (`EmitterHost.cs:62-65`).
- `output` — `string`, required. The relative output directory.

### 10.2 YAML shape

The user writes `.gravity.yaml` at the project root. (The legacy
`.gravity.config` filename is still accepted but emits a `CFG005`
deprecation warning — IDEs like Rider treat `.config` as XML, which is
why the canonical extension is now `.yaml`.) The C# emitter's real-world
block (`samples/registry/.gravity.yaml:1-5`):

```yaml
emitters:
  csharp:
    output: gen/csharp
    namespace: AcmeCo.Domain
    file_scoped_namespaces: true
```

A Kotlin emitter with one custom key would look like:

```yaml
emitters:
  kotlin:
    output: gen/kotlin
    package: com.acme.domain
  csharp:
    output: gen/csharp
    namespace: AcmeCo.Domain
```

### 10.3 Validation rules

| Rule id | Severity | Trigger |
| --- | --- | --- |
| `CFG001` | Warning | Unknown key in your emitter's config block. |
| `CFG002` | Error | Type mismatch — e.g. schema says `String`, YAML has a boolean. |
| `CFG003` | Error | Required key missing. |
| `CFG004` | Error | `output` is rooted or escapes the output root. |

These rules are emitted by `ConfigLoader` (`Gravity.Dsl.Emitter/ConfigLoader.cs`)
and `EmitterHost` (`EmitterHost.cs:71-98`). You do not implement them;
you declare a schema and the host validates against it.

### 10.4 Reading config inside `Emit`

`EmitterConfig.Values` is an `ImmutableSortedDictionary<string, object>`
where the value is typed per the schema's `ConfigValueKind`. Three
typed accessors are provided (`EmitterConfig.cs:18-50`):
`GetString`, `GetInt`, `GetBool`. They throw `InvalidOperationException`
when the key is absent or the wrong type — which only happens if you
have a schema bug.

The Outline sample factors the typed lens into a small projection class
(`OutlineEmitterConfig.cs:15-34`):

```csharp
public static OutlineEmitterConfig From(EmitterConfig config)
{
    if (config is null) throw new ArgumentNullException(nameof(config));
    return new OutlineEmitterConfig(config.GetString(OutlineEmitter.ConfigKeyOutput));
}
```

The reference C# emitter uses a `TryGet*` helper instead because every
key is optional with a default (`CSharpEmitter.cs:142-152`). Pick the
style that matches your schema — both are idiomatic.

### 10.5 Schema versioning conventions

The schema lives next to the emitter; bumping it follows the same
rules as the AST:

- Adding a new optional key with a sensible default is non-breaking.
  Existing configs continue to work.
- Adding a new required key is breaking. Ship a minor version of your
  emitter NuGet that prints a clear `CFG003` and document the
  migration.
- Renaming or removing a key is breaking. Same treatment.

There is no formal `schema_version` field; the emitter's NuGet version
is the authority. Consumers pin the emitter NuGet alongside their
`.gravity.yaml` (or the legacy `.gravity.config`).

---

## 11. NuGet packaging

### 11.1 Required properties

Mirror the Outline sample's csproj
(`samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Gravity.Dsl.Emitter.Sample.Outline.csproj:12-31`):

```xml
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
  <RootNamespace>Your.Emitter</RootNamespace>
  <AssemblyName>Your.Emitter</AssemblyName>

  <IsPackable>true</IsPackable>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>

  <Deterministic>true</Deterministic>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>

  <PackageId>Your.Emitter</PackageId>
  <Description>Kotlin emitter for the Gravity DSL.</Description>
  <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  <PackageTags>gravity dsl emitter kotlin</PackageTags>
</PropertyGroup>
```

The settings worth singling out:

- `IsPackable=true` — the project produces a `.nupkg`.
- `Deterministic=true` and `EmbedUntrackedSources=true` — pack
  determinism inherits from `Directory.Build.props` if you ship in the
  Gravity repo; restate them in your own repo to be explicit.
- `PackageLicenseExpression=Apache-2.0` — keep this aligned with the
  rest of the Gravity ecosystem.
- `GenerateDocumentationFile=true` — your XML doc surfaces in the
  consuming IDE and in NuGet.org's "API" panel.

### 11.2 Project references

```xml
<ItemGroup>
  <ProjectReference Include="path\to\Gravity.Dsl.Ast.csproj" />
  <ProjectReference Include="path\to\Gravity.Dsl.Emitter.csproj" />
</ItemGroup>
```

Or `<PackageReference>` against the published `Gravity.Dsl.Ast` and
`Gravity.Dsl.Emitter` NuGets once you ship outside the Gravity repo.
Both are 0.1.0 at time of writing (see their csprojs).

### 11.3 Cross-package wiring

For MSBuild auto-discovery (§4.2), ship a `buildTransitive/` props that
contributes your DLL to `<GravityDslEmitterAssembly>`. Verbatim from
the Outline sample:

```xml
<!-- buildTransitive/Your.Emitter.props -->
<Project>
  <ItemGroup>
    <GravityDslEmitterAssembly
      Include="$(MSBuildThisFileDirectory)..\lib\net9.0\Your.Emitter.dll" />
  </ItemGroup>
</Project>
```

And in your csproj:

```xml
<ItemGroup>
  <None Include="buildTransitive\Your.Emitter.props"
        Pack="true"
        PackagePath="buildTransitive\Your.Emitter.props" />
</ItemGroup>
```

The package id and the props file basename must match for NuGet's
auto-import to fire. The Outline sample is `Gravity.Dsl.Emitter.Sample.Outline`
and ships `buildTransitive/Gravity.Dsl.Emitter.Sample.Outline.props`.

---

## 12. CI / quality bar

A community emitter should ship with at least the following CI gates.

### Required tests

| Test | What it asserts |
| --- | --- |
| Golden-file (§9) | Output matches `tests/golden/<target>/` byte-for-byte. |
| Determinism | Two in-process runs against the same model produce the same bytes. |
| No-extra-files | Emitter produces only files present in goldens. |
| Empty-model smoke | Running against an empty `ResolvedModel` returns zero diagnostics and writes zero files. |
| Registration | `EmitterRegistry.FromInstances(new[] { new YourEmitter() })` returns zero discovery diagnostics. |

### CI matrix

```yaml
strategy:
  matrix:
    os: [ubuntu-latest, macos-latest]
```

Run `dotnet build` and `dotnet test` on both. Linux is the steady-state
target; macOS catches path-separator and locale drift.

### Analyzers

- `Microsoft.CodeAnalysis.BannedApiAnalyzers` must be active with
  `BannedSymbols.txt` attached. See §8.3.
- `TreatWarningsAsErrors=true` (inherited from `Directory.Build.props`
  if you ship in-repo; restate it otherwise).

### Conformance checklist

Before tagging a release:

- [ ] `IEmitter.TargetName` is set and stable across versions.
- [ ] `IEmitter.AnnotationNamespace` is non-empty and unique (or
      deliberately empty).
- [ ] `IEmitter.SupportedAstVersions` is a closed range like
      `">=1.0.0 <2.0.0"`.
- [ ] `IEmitter.ConfigurationSchema` declares every key the emitter
      reads in `Emit`.
- [ ] `Emit` iterates `model.Declarations` directly (no re-sorting).
- [ ] All file writes go through `IEmitterOutput.WriteFile`.
- [ ] All numeric `ToString` calls pass `CultureInfo.InvariantCulture`.
- [ ] All string-key sets use `StringComparer.Ordinal`.
- [ ] All sorted maps use `ImmutableSortedDictionary`.
- [ ] All line endings inside emitted strings are `\n`.
- [ ] Banned-API analyzer is active and the build is clean.
- [ ] Golden tests pass on Linux and macOS.
- [ ] Determinism test passes (two runs, byte-identical).
- [ ] NuGet package contains `lib/net9.0/Your.Emitter.dll` and
      `buildTransitive/Your.Emitter.props`.

---

## 13. End-to-end walkthrough — building a tiny emitter from scratch

Scenario: a `simple-text` emitter that writes one `.txt` file per
entity, each containing the entity's name and version on a single line.
Useful as a smoke target and as a step-by-step reference for new
authors.

Estimated reader time: 15 minutes.

### Step 1 — Project layout

```
simple-text-emitter/
├── Directory.Build.props
├── BannedSymbols.txt          # copied from gravity repo
├── src/
│   └── SimpleText/
│       ├── SimpleText.csproj
│       ├── SimpleTextEmitter.cs
│       └── buildTransitive/
│           └── SimpleText.props
└── tests/
    ├── SimpleText.Tests.csproj
    ├── GoldenFileTests.cs
    └── fixtures/
        └── golden/
            └── simple-text/
                └── ... (locked output)
```

### Step 2 — `SimpleText.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>

    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>

    <PackageId>SimpleText.Emitter</PackageId>
    <Description>Tiny demo emitter for the Gravity DSL. One .txt per entity.</Description>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
    <PackageTags>gravity dsl emitter text</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Gravity.Dsl.Ast" />
    <PackageReference Include="Gravity.Dsl.Emitter" />
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers"
                      PrivateAssets="all"
                      IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
    <AdditionalFiles Include="..\..\BannedSymbols.txt" />
  </ItemGroup>

  <ItemGroup>
    <None Include="buildTransitive\SimpleText.props"
          Pack="true"
          PackagePath="buildTransitive\SimpleText.Emitter.props" />
  </ItemGroup>

</Project>
```

Note `PackagePath="buildTransitive\SimpleText.Emitter.props"` — the
file basename in the package must match `PackageId`.

### Step 3 — `SimpleTextEmitter.cs`

```csharp
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;

namespace SimpleText;

/// <summary>
/// Demo emitter — one .txt file per entity declaring its name and version.
/// </summary>
public sealed class SimpleTextEmitter : IEmitter
{
    public const string ConfigKeyOutput = "output";

    public string TargetName => "simple-text";

    public string AnnotationNamespace => "simple-text";

    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");

    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput, ConfigValueKind.String, Required: true, Default: null)
    ));

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        string output = config.GetString(ConfigKeyOutput);

        foreach (var kv in model.Declarations)
        {
            if (kv.Value is not EntityDecl entity) continue;

            var body = new StringBuilder();
            body.Append(entity.Name);
            body.Append(' ');
            body.Append('v');
            body.Append(entity.Version.ToString(CultureInfo.InvariantCulture));
            body.Append('\n');

            string relPath = (output + "/" + entity.Name + ".txt").Replace('\\', '/');
            sink.WriteFile(relPath, body.ToString());
        }

        return new EmitResult(ImmutableArray<Diagnostic>.Empty);
    }
}
```

Notes:

- `entity.Version.ToString(CultureInfo.InvariantCulture)` is mandatory
  — `int.ToString()` without a culture is the bug §8.6 warns about.
- The `'\n'` is the only line ending. No `Environment.NewLine`.
- The relative path is `output + "/" + name + ".txt"`. Forward slashes
  only.

### Step 4 — `buildTransitive/SimpleText.props`

```xml
<Project>
  <ItemGroup>
    <GravityDslEmitterAssembly
      Include="$(MSBuildThisFileDirectory)..\lib\net9.0\SimpleText.dll" />
  </ItemGroup>
</Project>
```

### Step 5 — Golden-file test

`tests/GoldenFileTests.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using SimpleText;
using Xunit;

namespace SimpleText.Tests;

public sealed class GoldenFileTests
{
    [Fact]
    public async Task EveryEntity_EmitsOneTxt()
    {
        var model = LoadFixtureModel(); // your helper — compile the .gravity inputs
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new SimpleTextEmitter() });
        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["simple-text"] = new EmitterConfig(
                TargetName: "simple-text",
                Enabled: true,
                Output: "gen/simple-text",
                Values: ImmutableSortedDictionary<string, object>.Empty
                    .Add("output", "gen/simple-text"))
        };

        var run = await EmitterHost.Run(model, configs, registry, outputRoot: null);
        run.Diagnostics.Should().BeEmpty();

        var emitted = run.EmitterBuffers["simple-text"].Snapshot();
        var goldenRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "golden", "simple-text");
        foreach (var goldenPath in Directory.GetFiles(goldenRoot, "*.txt", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(goldenRoot, goldenPath).Replace('\\', '/');
            emitted.Keys.Should().Contain(rel);
            var goldenText = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            emitted[rel].Should().Be(goldenText);
        }
    }
}
```

Implement `LoadFixtureModel()` as the same helper the Gravity test
suite uses — feed a directory of `.gravity` files through the
compiler's `Parser` + `Resolver` and return the resulting
`ResolvedModel`. The Gravity test project's `SamplesLoader` is the
canonical pattern.

### Step 6 — Pack and consume

```bash
dotnet pack src/SimpleText/SimpleText.csproj -c Release -o ./nupkg
```

In a consuming `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Gravity.Dsl.MsBuild" />
  <PackageReference Include="SimpleText.Emitter" />
</ItemGroup>
```

Add a `.gravity.yaml` next to the consumer csproj:

```yaml
emitters:
  simple-text:
    output: gen/simple-text
```

Run `dotnet build`. The MSBuild target picks up the
`<GravityDslEmitterAssembly>` from your package's buildTransitive
props, loads `SimpleText.dll`, registers `SimpleTextEmitter`, and your
`.txt` files appear under `obj/Generated/simple-text/`.

---

## 14. Common pitfalls

### Forgetting a schema entry

You read `config.GetString("foo")` inside `Emit`, but you never added
`"foo"` to `ConfigurationSchema`. Two failure modes: the loader emits
`CFG001` for the unknown key in the user's YAML (warning, you miss
it), or, more likely, the user puts the key in and the loader silently
drops it because it is not in your schema; your `GetString` throws.
Always declare every key you read.

### `DateTime.UtcNow` / `Guid.NewGuid`

The banned-API analyzer catches both at build time (§8.2). If you saw
this fail, you are reading the docs in the right order — you do not
need an environment-dependent value to render a `.cs`, a `.kt`, or
a `.yaml`. The compiler hands you a stable `ResolvedModel`; the
output is a deterministic function of it.

### `Environment.NewLine` in emitted contents

Slips in through `StringBuilder.AppendLine`, through
`StringWriter.WriteLine`, through interpolated strings with literal
`\r\n`. The on-disk file looks fine on Windows; the byte-compare on
Linux CI fails. The fix is to always concatenate `'\n'` explicitly
(`body.Append('\n')` in §13 step 3) or to override `NewLine = "\n"` on
every `TextWriter` you create.

### Iterating `Dictionary<TKey, TValue>` to emit

`Dictionary<T,U>` iteration order is unspecified and changes across
runtime versions. The moment your output is sensitive to iteration
order (file names, sorted lists in generated code, dictionary keys in
emitted JSON), the runtime can produce a different byte sequence and
the determinism test fails. Replace with
`ImmutableSortedDictionary<TKey, TValue>` (ordinal comparer for string
keys) at every iteration point.

### Pattern-matching `NamedTypeRef` positionally

Tempting:

```csharp
case NamedTypeRef(var name, var isOpt, var isArr, var span):
```

Phase 8 added a fifth positional parameter (`int? Version`). The above
no longer compiles against `AstVersion.Value = 1.1.0`. Use named
properties:

```csharp
case NamedTypeRef n:
    var name  = n.Name;
    var isOpt = n.IsOptional;
    var isArr = n.IsArray;
    var ver   = n.Version; // null if no @N
```

This is what `Gravity.Dsl.Ast/README.md` calls out explicitly:
"Pattern matching using named properties is unaffected" by AST minor
bumps. Positional deconstruction is **not** part of the additive
contract.

---

## 15. Reference: existing emitters

### C# (Phase 3, production reference)

- Path: `Gravity.Dsl.Emitter.CSharp/CSharpEmitter.cs`
- Target name: `csharp`
- Annotation namespace: `csharp`
- AST range: `>=1.0.0 <2.0.0`
- Status: ships in the box. Used by `Gravity.Dsl.MsBuild` as the
  default emitter wired into `GravityDslGenerate`.
- Reads `csharp(...)` annotations to drive optional emit behaviour
  (e.g. `[Serializable]`).

Use it as the production-grade reference. It is the longest and
demonstrates the idioms: file-scoped namespaces, doc-comment headers,
state-enum + events file + commands file per entity, namespace-mapped
directory layout.

### Outline (Phase 9b sample, minimal template)

- Path: `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/OutlineEmitter.cs`
- Target name: `outline`
- Annotation namespace: `outline`
- AST range: `>=1.0.0 <2.0.0`
- Status: sample, not a production target. The `.Sample.` segment in
  the NuGet id is permanent (LD-12). Do not extend in place — copy and
  start a sibling project.

Use it as the copy-paste template. It is deliberately tiny. The shape
mirrors the C# emitter so a reader of the sample recognises the
idioms when they later read the production code.

### Future emitters (not yet shipped)

Per the phase roadmap:

| Target | Phase | Status |
| --- | --- | --- |
| JSON Schema | Phase 4 | Planned |
| GraphQL | Phase 5 | Planned |
| OpenAPI | Phase 6 | Planned |
| AsyncAPI | Phase 7 | Planned |

The grammar is stable enough that you can ship any of these as a
community emitter today. The phase numbering reflects the order the
reference set will land, not a dependency.

### Project layout cheatsheet

```
Gravity.Dsl.Ast/                  # AST records (this is what you read)
Gravity.Dsl.Emitter/              # IEmitter + host + registry + sink + config loader
Gravity.Dsl.Emitter.CSharp/       # Reference C# emitter
Gravity.Dsl.Cli/                  # gravity-dsl CLI (consumes the host)
Gravity.Dsl.MsBuild/              # MSBuild integration (consumes the host)
samples/emitters/outline/         # Sample emitter — copy-paste template
samples/registry/                 # Sample Gravity source + .gravity.yaml
tests/fixtures/                   # Shared .gravity inputs
tests/golden/                     # Locked emitter output per target
```

---

## 16. Glossary

| Term | Meaning |
| --- | --- |
| **AST** | The public, versioned `Gravity.Dsl.Ast` record graph. `AstVersion.Value = "1.1.0"`. |
| **AstVersion** | The semantic-version string identifying the AST contract. Independent of the DSL grammar version. |
| **FQN** | Fully qualified name — the dot-joined `<namespace>.<declaration name>`. E.g. `hr.Employee`. The primary key into `ResolvedModel.Declarations`. |
| **Resolved Model** | `Gravity.Dsl.Compiler.Resolution.ResolvedModel`. The post-resolution view of a Gravity program. Sorted, validated, references bound. The only thing emitters consume. |
| **DeclKey** | `(Fqn, Version)` composite key for the multi-version declaration map. Comparable; ordering is `(Fqn ordinal asc, Version asc)`. |
| **Annotation namespace** | The vocabulary an emitter owns (`csharp`, `kotlin`, `json-schema`). Annotations are scoped to a namespace; only the owning emitter reads them. |
| **Target name** | The emitter's stable identifier (`csharp`, `outline`). Used by the Gravity config file (`.gravity.yaml`; legacy `.gravity.config`) to address the emitter's block. |
| **Emitter host** | `EmitterHost.Run(...)`. Coordinates pre-flight checks, parallel emitter invocation, diagnostic sorting, and deterministic commit-to-disk. |
| **Sink** | `IEmitterOutput`. The buffered output an emitter writes through. The host owns the implementation. |
| **Golden** | A locked-in expected emitter output file under `tests/golden/<target>/`. Byte-compared against fresh emitter output on every CI run. |
| **HOST001 / HOST002 / HOST003 / HOST004** | Host-level diagnostics: AST incompatibility, annotation namespace collision, output directory overlap, emitter threw. |
| **CFG001 / CFG002 / CFG003 / CFG004** | Config-loader diagnostics: unknown key (warning), type mismatch, required missing, unsafe output path. |
| **FR-…** | Functional requirement identifier from the phase specs (`specs/001-…`, `specs/002-…`, `specs/003-…`). Citations in this guide point back to the spec where a behaviour was decided. |
| **Principle I / III / VI** | Constitutional principles from `CLAUDE.md`. I: "The DSL is the spec". III: "Read-only at build time". VI: "Pluggable, not prescriptive". The contractual reason most of this guide exists. |
