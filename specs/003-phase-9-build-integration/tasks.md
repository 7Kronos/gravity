# Gravity DSL — Task Plan (Phase 9: Build integration + reference sample emitter)

**Status:** Locked for implementation
**Date:** 2026-05-18
**Driven by:** `specs/003-phase-9-build-integration/plan.md`
**Predecessor:** `specs/002-phase-8-versioning/tasks.md` (T100..T172). Phase 9 task ids begin at `T200` to keep the global task-id namespace flat and grep-friendly.

Conventions:
- Tasks numbered `T2##` in execution order. Sub-phase boundaries (P9a → P9b → P9c) are hard gates; a sub-phase's tasks complete before the next sub-phase begins.
- `[P]` marks tasks runnable in parallel with peers in the **same** sub-phase.
- Every task lists: **Acceptance** (verifiable; cites FR/AC from `spec.md`), **Files** (repo-relative paths touched), **Depends on** (prior T-numbers or `—`).
- Phase 0–8 tasks (T001..T172) remain locked. No Phase 9 task removes or weakens a Phase 0–8 acceptance condition.

---

## Sub-phase P9a — MSBuild target (T199–T219)

Goal: package `Gravity.Dsl.MsBuild` as a NuGet with build assets (`.props`, `.targets`, task assembly + dependency closure) that any csproj can consume via `<PackageReference>`. The task wraps `CompilerPipeline.Gen` so behaviour is identical to the CLI (LD-11). Closes FR-200..FR-213, FR-230..FR-242, FR-250..FR-251, AC-9.1, AC-9.15.

### T199. Promote CLI helper types to public (LD-13)
- **Acceptance.** Change `internal static class CompilerPipeline` (currently `Gravity.Dsl.Cli/CompilerPipeline.cs` line 21) to `public static class CompilerPipeline`. Promote the nested `PipelineResult` record to `public` if it isn't already. Change `internal static class DiagnosticFormatter` (`Gravity.Dsl.Cli/DiagnosticFormatter.cs` line 11) to `public`. Change `internal static class CliRuleIds` (`Gravity.Dsl.Cli/CliRuleIds.cs`) to `public`. Leave `Program` (`Gravity.Dsl.Cli/Program.cs`) as `internal` — it is an entry-point, not a stable API. All existing tests continue to pass; the diff is just `internal` → `public`. No callers need rewriting because everything currently calling these from inside the same assembly continues to work. This promotion is the LD-13 prerequisite for the MSBuild task assembly to call into the CLI helper surface without `InternalsVisibleTo` chains.
- **Files.** `Gravity.Dsl.Cli/CompilerPipeline.cs`, `Gravity.Dsl.Cli/DiagnosticFormatter.cs`, `Gravity.Dsl.Cli/CliRuleIds.cs`.
- **Depends on.** —

### T199b. Roslyn collision pre-spike (gating)
- **Acceptance.** Build a minimal `tests/integration/msbuild-smoke/` consumer with `Gravity.Dsl.MsBuild` and the existing C# emitter; run `dotnet build` and verify no `FileLoadException` / `MissingMethodException` / version-mismatch warning from Roslyn surfaces in the build log. If smoke fails: flip the implementation default to OutOfProc per the risk mitigation in plan.md §6 (Roslyn ALC row). Document the result in plan.md §6 (one-paragraph appendix recording whether InProc remained the default or OutOfProc was promoted). This is a gating spike, not a real implementation task — it just decides which path P9a takes for the remaining T200..T254 work. **No further P9a tasks (T200+) start until T199b's result is recorded.**
- **Files.** `tests/integration/msbuild-smoke/` (minimal scaffold), `specs/003-phase-9-build-integration/plan.md` (§6 appendix line).
- **Depends on.** T199.

### T200. Scaffold `Gravity.Dsl.MsBuild/` csproj
- **Acceptance.** New project `Gravity.Dsl.MsBuild/Gravity.Dsl.MsBuild.csproj` targets `net9.0`. Property group sets `<IsPackable>true</IsPackable>`, `<IncludeBuildOutput>false</IncludeBuildOutput>` (task assembly goes under `tasks/`, not `lib/`), `<GenerateDocumentationFile>true</GenerateDocumentationFile>`. Builds cleanly with `dotnet build` against the existing solution.
- **Files.** `Gravity.Dsl.MsBuild/Gravity.Dsl.MsBuild.csproj`.
- **Depends on.** —

### T201 [P]. Pin Microsoft.Build dependencies
- **Acceptance.** `Gravity.Dsl.MsBuild.csproj` references `Microsoft.Build.Utilities.Core` with `<PackageReference Include="Microsoft.Build.Utilities.Core" Version="17.x" PrivateAssets="all" ExcludeAssets="runtime" />`. `ExcludeAssets="runtime"` is the load-bearing detail: MSBuild's `AssemblyLoadContext` provides its own copy of this assembly, so bundling it under `tasks/net9.0/` would produce ALC collision warnings. Compile-time visibility is preserved by the default `compile` asset; the `runtime` asset is the DLL that would otherwise be staged into `bin/$(Configuration)/net9.0/` and swept into the pack glob. Verify by `unzip -l` on the resulting .nupkg: `Microsoft.Build.Utilities.Core.dll` MUST NOT appear under `tasks/net9.0/`. All Phase 0-8 compiler+emitter dependencies (Pidgin, YamlDotNet, Microsoft.CodeAnalysis.CSharp.Workspaces, System.CommandLine) flow through `ProjectReference` to `Gravity.Dsl.Compiler`, `Gravity.Dsl.Emitter`, `Gravity.Dsl.Emitter.CSharp`, `Gravity.Dsl.Cli` (the last for `CompilerPipeline` — promoted to public per T199). Use `PrivateAssets="all"` where appropriate so dependencies are bundled but not transitively exposed.
- **Files.** `Gravity.Dsl.MsBuild/Gravity.Dsl.MsBuild.csproj`, `Directory.Packages.props` (add Microsoft.Build.Utilities.Core pin).
- **Depends on.** T199, T200.

### T202 [P]. Extend `Directory.Build.props` `<Choose>/<When>` for BannedSymbols carve-out
- **Acceptance.** The existing Phase 8 carve-out (which exempts `Gravity.Dsl.Cli` from `BannedSymbolsFile`) is extended to also exempt `Gravity.Dsl.MsBuild`. The list is exact-equality only (no prefix/substring matching) so a future `Gravity.Dsl.MsBuild.X` sibling would NOT be exempted by accident. Add a one-line comment naming the three exempt projects (`Cli`, `Tests`, `MsBuild`) and the rationale (each contains the one allowed `DateTime.UtcNow` call site for date defaulting).
- **Files.** `Directory.Build.props`.
- **Depends on.** —

### T203. `Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.props`
- **Acceptance.** Declares the `<GravityDsl>` item type. Sets default properties: `<GravityDslDefaultIncludePattern>**/*.gravity</GravityDslDefaultIncludePattern>`, `<GravityDslOutputDir>$(IntermediateOutputPath)Generated</GravityDslOutputDir>`. Adds a conditional default include: `<GravityDsl Include="$(GravityDslDefaultIncludePattern)" Condition="'@(GravityDsl)' == ''" />`. The file lives under `buildTransitive/` only — no legacy `build/` mirror is shipped (modern NuGet 5.0+, every .NET SDK since 3.0, handles `buildTransitive/` for both direct and transitive consumers). Per plan.md §3.1.
- **Files.** `Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.props`.
- **Depends on.** T200.

### T204. `Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.targets`
- **Acceptance.** `<UsingTask TaskName="GravityDslGenTask" AssemblyFile="$(MSBuildThisFileDirectory)../tasks/net9.0/Gravity.Dsl.MsBuild.dll" />`. Declares `<Target Name="GravityDslGenerate" BeforeTargets="CoreCompile" Condition="'@(GravityDsl)' != ''">` invoking the task with `Sources="@(GravityDsl)"`, `OutputDir="$(GravityDslOutputDir)"`, `ConfigFile="$(GravityDslConfig)"`, `AsOf="$(GravityDslAsOf)"`. After the task: `<ItemGroup><Compile Include="$(GravityDslOutputDir)/csharp/**/*.cs" /></ItemGroup>` so generated files participate in the build (FR-251). T204b adds the `Inputs`/`Outputs` incremental-build keys; this task delivers the initial target structure without them. Per plan.md §3.2.
- **Files.** `Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.targets`.
- **Depends on.** T203.

### T204b. Incremental-build keys on `GravityDslGenerate` (FR-208)
- **Acceptance.** Extend `GravityDslGenerate` from T204 with `Inputs="@(GravityDsl)"` and `Outputs="$(GravityDslOutputDir)\.gravity-stamp"`. Add a final `<Touch Files="$(GravityDslOutputDir)\.gravity-stamp" AlwaysCreate="true" />` task step. Register the stamp file with `<FileWrites Include="$(GravityDslOutputDir)\.gravity-stamp" />` so `dotnet clean` removes it. Per plan.md §3.2 target XML. This enables MSBuild's standard incremental-build short-circuit: when no `.gravity` source has a newer mtime than the stamp, the target is skipped entirely on subsequent builds (AC-9.15).
- **Files.** `Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.targets` (extend T204).
- **Depends on.** T204.

### T205 [P]. Ship `buildTransitive/` only (no `build/` mirror)
- **Acceptance.** The package ships `buildTransitive/Gravity.Dsl.MsBuild.props` and `buildTransitive/Gravity.Dsl.MsBuild.targets` only; do NOT populate `build/`. Modern NuGet (5.0+, every .NET SDK since 3.0) handles `buildTransitive/` for both direct and transitive consumers correctly, so mirroring is dead weight. A pack-content test (T213) asserts the entry set contains no `build/` entries.
- **Files.** (no new files; this is a contract row that T212/T213 enforce — no separate scaffold needed)
- **Depends on.** T203, T204.

### T206. `GravityDslGenTask.cs` — Microsoft.Build.Utilities.Task wrapper
- **Acceptance.** `Gravity.Dsl.MsBuild/tasks/GravityDslGenTask.cs` declares `public sealed class GravityDslGenTask : Microsoft.Build.Utilities.Task`. Properties: `[Required] ITaskItem[] Sources`, `[Required] string OutputDir`, `string ConfigFile`, `string AsOf`. `Execute()` resolves `currentDate` (T209), then **groups `Sources` by `(item.GetMetadata("Output") ?? OutputDir, item.GetMetadata("Emitter") ?? "")` and invokes `CompilerPipeline.Gen(inputs, outputRoot, emitterFilter, configFile, currentDate)` once per group** (FR-202 — per-item Output/Emitter override). On any group's error-severity diagnostic, log it through `Log.LogError` and return `false`; otherwise return `true`. Per plan.md §3.3 pseudocode. Parity (LD-11): no parallel re-implementation of the generation path; the public `CompilerPipeline.Gen` (T199) is the single entry. If `CompilerPipeline.Gen` does not yet accept an `IList<string>` of explicit sources, add an additive overload as part of this task (the overload is part of the LD-13 public surface and inherits its stability contract — see FR-234). T243 / T250 cover the per-group dispatch by fixture.
- **Files.** `Gravity.Dsl.MsBuild/tasks/GravityDslGenTask.cs`, `Gravity.Dsl.Cli/CompilerPipeline.cs` (additive `Gen` overload accepting `IList<string> inputs`).
- **Depends on.** T199, T201, T202.

### T207. Diagnostic formatting in MSBuild canonical form
- **Acceptance.** `GravityDslGenTask.Execute` routes Diagnostic objects to `Log.LogError(...)` / `Log.LogWarning(...)` / `Log.LogMessage(MessageImportance.Low, ...)` per severity (FR-241). The text format is the MSBuild-canonical `path(line,col): <severity> <ruleId>: <message>` (FR-240) so IDE click-through works. The path used is the absolute resolved path of the offending `.gravity` source. Rule ids preserved verbatim (PARSE..., RES..., VAL..., CFG..., HOST...).
- **Files.** `Gravity.Dsl.MsBuild/tasks/GravityDslGenTask.cs` (extend).
- **Depends on.** T206.

### T208 [P]. `MsBuildDateResolver` shared helper + `InternalsVisibleTo`
- **Acceptance.** Per plan.md §3.8: introduce one shared `internal static MsBuildDateResolver.TryResolve(string? rawAsOf, out DateOnly result, out string? error)` in `Gravity.Dsl.Cli/MsBuildDateResolver.cs`. The existing CLI `TryResolveAsOf` and the new `GravityDslGenTask.Execute` both call this helper. Add `[InternalsVisibleTo("Gravity.Dsl.MsBuild")]` to `Gravity.Dsl.Cli/AssemblyInfo.cs`. The helper centralises the one allowed `DateTime.UtcNow` read on the build side.
- **Files.** `Gravity.Dsl.Cli/MsBuildDateResolver.cs` (new), `Gravity.Dsl.Cli/Program.cs` (call site update), `Gravity.Dsl.Cli/AssemblyInfo.cs`.
- **Depends on.** T202.

### T209. GravityDslGenTask reads `<GravityDslAsOf>` MSBuild property
- **Acceptance.** When `AsOf` task property is non-empty, parse via `MsBuildDateResolver`; on failure log `MSB003: --as-of value '<X>' must be YYYY-MM-DD` (or similar) and return `false`. When empty, default to today via the same helper. The Phase 8 deprecation-window check (VAL030) flows through unchanged because `currentDate` is passed to `CompilerPipeline.Gen` (FR-233).
- **Files.** `Gravity.Dsl.MsBuild/tasks/GravityDslGenTask.cs` (extend).
- **Depends on.** T206, T208.

### T210 [P]. `MsBuildRuleIds.cs` — reserved rule ids MSB001..MSB010
- **Acceptance.** New file `Gravity.Dsl.MsBuild/MsBuildRuleIds.cs` declares `public static class MsBuildRuleIds` with constants `Msb001..Msb010`. Comments document each: `MSB001` (task internal error), `MSB002` (missing config), `MSB003` (malformed `<GravityDslAsOf>`), `MSB004` (OutputDir parent-traversal), etc. Per FR-242. Reserve unused ids for future use (Phase 9b authoring guide may name additional ids).
- **Files.** `Gravity.Dsl.MsBuild/MsBuildRuleIds.cs`.
- **Depends on.** T200.

### T211. NuGet packaging metadata
- **Acceptance.** `Gravity.Dsl.MsBuild.csproj` sets `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>` (FR-212; required because MSBuild tasks load against MSBuild's own AssemblyLoadContext, so every bundled dependency must be co-located under `tasks/net9.0/`). Sets `<PackageId>Gravity.Dsl.MsBuild</PackageId>`, `<Description>`, `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>`, `<RepositoryUrl>`, `<PackageTags>gravity dsl msbuild</PackageTags>`.
- **Files.** `Gravity.Dsl.MsBuild/Gravity.Dsl.MsBuild.csproj`.
- **Depends on.** T206.

### T212 [P]. Pack content layout
- **Acceptance.** In `Gravity.Dsl.MsBuild.csproj`: `<None Include="buildTransitive/**" Pack="true" PackagePath="buildTransitive/" />` (no legacy `build/` mirror — FR-210). The task assembly + its full dependency closure (Gravity.Dsl.Compiler, .Ast, .Emitter, .Emitter.CSharp, `gravc.dll` (the CLI assembly per its `AssemblyName`), Pidgin, YamlDotNet, Microsoft.CodeAnalysis.*) lands under `tasks/net9.0/` via a custom pack target that copies the build output. `Microsoft.Build.Utilities.Core.dll` MUST NOT land under `tasks/net9.0/` (T201 mandates `ExcludeAssets="runtime"` to keep it out of the build output that the pack glob picks up).
- **Files.** `Gravity.Dsl.MsBuild/Gravity.Dsl.MsBuild.csproj` (extend).
- **Depends on.** T211.

### T213. Pack content test (AC-9.1)
- **Acceptance.** A test under `Gravity.Dsl.Tests/MsBuild/PackContentTests.cs` runs `dotnet pack Gravity.Dsl.MsBuild --output <temp>`, opens the resulting `.nupkg` via `System.IO.Compression.ZipArchive`, and asserts the presence of: `buildTransitive/Gravity.Dsl.MsBuild.props`, `buildTransitive/Gravity.Dsl.MsBuild.targets`, `tasks/net9.0/Gravity.Dsl.MsBuild.dll`, `tasks/net9.0/Gravity.Dsl.Compiler.dll`, `tasks/net9.0/Gravity.Dsl.Ast.dll`, `tasks/net9.0/Gravity.Dsl.Emitter.dll`, `tasks/net9.0/Gravity.Dsl.Emitter.CSharp.dll`, `tasks/net9.0/gravc.dll` (the CLI assembly per its `AssemblyName`; FR-234 — NOT `Gravity.Dsl.Cli.dll`), `tasks/net9.0/Pidgin.dll`, `tasks/net9.0/YamlDotNet.dll`. ALSO asserts the absence of: any `build/` entries (FR-210 — `buildTransitive/` only); `tasks/net9.0/Microsoft.Build.Utilities.Core.dll` (FR-210 — MSBuild's own ALC provides this assembly; bundling it causes collision warnings). No shell-out — pure managed code.
- **Files.** `Gravity.Dsl.Tests/MsBuild/PackContentTests.cs`.
- **Depends on.** T212.

### T214. Deterministic pack test (FR-212)
- **Acceptance.** A test runs `dotnet pack` twice (back-to-back, separate output dirs) and asserts SHA-256 byte-equality of the two `.nupkg`s. Marked `[Trait("Category", "Slow")]` so it can be filtered out of fast lanes.
- **Files.** `Gravity.Dsl.Tests/MsBuild/DeterministicPackTests.cs`.
- **Depends on.** T213.

### T215. Wire `Gravity.Dsl.MsBuild` into the solution
- **Acceptance.** `Gravity.Dsl.sln` gains the new project via `dotnet sln add`. The solution still builds cleanly. The MSBuild project is **not** referenced by `Gravity.Dsl.Tests` directly (it's consumed via NuGet in the smoke tests under P9c).
- **Files.** `Gravity.Dsl.sln`.
- **Depends on.** T200.

---

## Sub-phase P9b — Sample emitter (T220–T234)

Goal: ship a deliberately minimal "outline" emitter (Markdown summary per entity) as a NuGet package alongside the C# reference emitter. Demonstrates the IEmitter contract end-to-end for community authors. Closes FR-220..FR-225, AC-9.6, AC-9.7.

### T220. Scaffold `Gravity.Dsl.Emitter.Sample.Outline` csproj
- **Acceptance.** New project at `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Gravity.Dsl.Emitter.Sample.Outline.csproj` targets `net9.0`. ProjectReferences `Gravity.Dsl.Ast`, `Gravity.Dsl.Emitter`. NuGet metadata: `<IsPackable>true</IsPackable>`, package id, description ("sample emitter — minimal IEmitter implementation"), Apache-2.0 license.
- **Files.** `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Gravity.Dsl.Emitter.Sample.Outline.csproj`.
- **Depends on.** —

### T221 [P]. `samples/emitters/outline/README.md`
- **Acceptance.** One-paragraph README: "This sample emitter is intentionally minimal — it exists as a copy-paste template for community emitters, not a production tool. It emits one Markdown file per entity summarizing its identity, relations, properties, lifecycle, events, and commands. See the emitter authoring guide (Phase 9b) for the full IEmitter contract walkthrough."
- **Files.** `samples/emitters/outline/README.md`.
- **Depends on.** —

### T222. OutlineEmitter implements IEmitter
- **Acceptance.** `OutlineEmitter.cs` declares `public sealed class OutlineEmitter : IEmitter`. `TargetName => "outline"`. `AnnotationNamespace => "outline"`. `SupportedAstVersions => SemanticVersionRange.Parse(">=1.0.0 <2.0.0")`. `ConfigurationSchema` declares one required key: `output` (string). The class is `sealed` (Principle III); no `partial`, no `virtual`.
- **Files.** `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/OutlineEmitter.cs`.
- **Depends on.** T220.

### T223. OutlineEmitterConfig — YAML deserialisation
- **Acceptance.** `OutlineEmitterConfig.cs` deserialises the `output` key from a YAML emitter section using the same YamlDotNet machinery as `CSharpEmitterConfig`. Missing `output` produces a `CFG003` (required-key-missing) diagnostic at host startup, consistent with the existing emitter-host behaviour.
- **Files.** `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/OutlineEmitterConfig.cs`.
- **Depends on.** T222.

### T224. EntityOutlineRenderer — Markdown for entities
- **Acceptance.** `Render/EntityOutlineRenderer.cs` emits one `.md` file per `EntityDecl` to `<output>/<namespace path>/<EntityName>.md`. Structure:
  - `# <Name>@<Version>` (heading)
  - `## Identity` — table of name, type
  - `## Relations` — table of name, target, cardinality, optionality, semantic role
  - `## Properties` — table of name, type, optional/array modifiers
  - `## Lifecycle` — two subsections: `### States` (bullet list in declaration order) and `### Transitions` (table from/to/on)
  - `## Events` — one subsection per event with payload table
  - `## Commands` — one subsection per command with argument table + `returns` and `with side_effect`
  Format determinism: declaration order preserved, no timestamps, LF line endings (FR-222 / FR-250). Use `int.ToString(CultureInfo.InvariantCulture)` for any integer rendering.
- **Files.** `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Render/EntityOutlineRenderer.cs`.
- **Depends on.** T222.

### T225 [P]. ValueType + Enum rendering
- **Acceptance.** Minimal `.md` output for `ValueTypeDecl` (heading + field table) and `EnumDecl` (heading + variant bullet list). Filenames `<Name>.md` under the same namespace path. Renderers in `Render/ValueTypeOutlineRenderer.cs` and `Render/EnumOutlineRenderer.cs`. FR-221 only mandates entities; these are easy and complete the round-out.
- **Files.** `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Render/ValueTypeOutlineRenderer.cs`, `EnumOutlineRenderer.cs`.
- **Depends on.** T222.

### T226. Determinism wiring
- **Acceptance.** OutlineEmitter and renderers use `StringBuilder` with explicit `'\n'` line endings (never `Environment.NewLine`). Banned-APIs analyzer is active on this project (it's not in the carve-out list); a build that introduces `DateTime.UtcNow` or `Guid.NewGuid` would fail. AC-9.7 (byte-determinism across runs) is pinned by the test in T230.
- **Files.** (no new files; integrated into T222/T224/T225)
- **Depends on.** T224, T225.

### T227. Wire `Gravity.Dsl.Emitter.Sample.Outline` into the solution
- **Acceptance.** `Gravity.Dsl.sln` gains the new project. Builds cleanly.
- **Files.** `Gravity.Dsl.sln`.
- **Depends on.** T220.

### T228 [P]. Golden files for samples/registry entities
- **Acceptance.** `tests/golden/outline/hr/Employee.md`, `TimeEntry.md`, `Project.md` mirror the existing Phase 0-3 samples. Files are hand-derived from the expected Markdown shape; the harness at T229 byte-compares them. LF line endings only.
- **Files.** `tests/golden/outline/hr/Employee.md`, `TimeEntry.md`, `Project.md`.
- **Depends on.** T224.

### T229 [P]. OutlineGoldenFileTests
- **Acceptance.** `Gravity.Dsl.Tests/Emitter/Outline/GoldenFileTests.cs` runs OutlineEmitter against `samples/registry/*.gravity` and byte-compares output against `tests/golden/outline/`. Mirrors the existing `Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs` pattern. Updating goldens requires `UPDATE_GOLDEN=1` env var (matches the Phase 8 golden mechanism).
- **Files.** `Gravity.Dsl.Tests/Emitter/Outline/GoldenFileTests.cs`.
- **Depends on.** T224, T228.

### T230. OutlineDeterminismTests (AC-9.7)
- **Acceptance.** Runs the OutlineEmitter twice in a single test method against `samples/registry/`; asserts byte-identical output buffers. Mirrors `Gravity.Dsl.Tests/Emitter/CSharp/DeterminismTests.cs`.
- **Files.** `Gravity.Dsl.Tests/Emitter/Outline/DeterminismTests.cs`.
- **Depends on.** T229.

### T231 [P]. OutlineEmitterRegistrationTests
- **Acceptance.** Uses `EmitterRegistry` to discover the OutlineEmitter from a test plugin directory. Asserts `TargetName == "outline"`, `AnnotationNamespace == "outline"`, supported AST range admits `1.1.0`. Mirrors existing Phase 2 stub-emitter tests.
- **Files.** `Gravity.Dsl.Tests/Emitter/Outline/RegistrationTests.cs`.
- **Depends on.** T222.

### T232 [P]. Document outline in samples/registry/.gravity.config (commented-out example)
- **Acceptance.** Add a commented-out `# outline:` block to `samples/registry/.gravity.config` showing the configuration shape. This documents the sample without breaking the existing T172 csharp-only smoke flow.
- **Files.** `samples/registry/.gravity.config`.
- **Depends on.** T223.

### T233. NuGet packaging metadata for sample emitter
- **Acceptance.** `Gravity.Dsl.Emitter.Sample.Outline.csproj` sets `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`. Package description: "Sample emitter for Gravity DSL — emits Markdown outlines. Intentionally minimal; copy-paste template for community emitters."
- **Files.** `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Gravity.Dsl.Emitter.Sample.Outline.csproj`.
- **Depends on.** T222.

### T234. Pack content test for sample emitter
- **Acceptance.** Mirrors T213: `dotnet pack` the sample emitter; assert the resulting `.nupkg` contains `lib/net9.0/Gravity.Dsl.Emitter.Sample.Outline.dll`. No extraneous files.
- **Files.** `Gravity.Dsl.Tests/Emitter/Outline/PackContentTests.cs`.
- **Depends on.** T233.

---

## Sub-phase P9c — Integration smoke + parity (T240–T254)

Goal: validate that the MSBuild package and sample emitter actually work end-to-end when consumed via `<PackageReference>`. Closes AC-9.2, AC-9.3, AC-9.4, AC-9.5, AC-9.8, AC-9.9, AC-9.10, AC-9.11, AC-9.12, AC-9.13, AC-9.14.

### T240. Scaffold `tests/integration/msbuild-smoke/Smoke.csproj`
- **Acceptance.** A minimal `net9.0` ConsoleApp csproj that references `Gravity.Dsl.MsBuild` via a local-packages NuGet feed (a `nuget.config` redirects to `bin/local-packages/` populated by `dotnet pack` of T211). The csproj declares `<GravityDsl Include="registry/**/*.gravity" />` (relying on the default include, but explicit for clarity).
- **Files.** `tests/integration/msbuild-smoke/Smoke.csproj`, `tests/integration/msbuild-smoke/nuget.config`.
- **Depends on.** T215.

### T241 [P]. Smoke fixture .gravity.config
- **Acceptance.** `tests/integration/msbuild-smoke/.gravity.config` enables only the `csharp` emitter (matches the Phase 0-3 default). Output path: `gen/csharp`.
- **Files.** `tests/integration/msbuild-smoke/.gravity.config`.
- **Depends on.** —

### T242 [P]. Smoke fixture .gravity sources
- **Acceptance.** `tests/integration/msbuild-smoke/registry/Employee.gravity` declares a minimal entity (one identity + one property + one event + one command) sufficient to exercise the emitter pipeline end-to-end.
- **Files.** `tests/integration/msbuild-smoke/registry/Employee.gravity`.
- **Depends on.** —

### T243. MsBuildSmokeTests harness (AC-9.2)
- **Acceptance.** `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` runs (in test setup) `dotnet pack Gravity.Dsl.MsBuild --output bin/local-packages` then `dotnet build tests/integration/msbuild-smoke/Smoke.csproj`. Asserts (a) exit code 0, (b) generated `.cs` files exist under `tests/integration/msbuild-smoke/obj/Generated/csharp/`, (c) those `.cs` files compile in a throwaway harness (re-use the T052 mechanism). Marked `[Trait("Category", "Slow")]`.
- **Files.** `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs`.
- **Depends on.** T240, T241, T242.

### T244. Negative test: parse error → MSBuild error (AC-9.3)
- **Acceptance.** A second fixture `tests/integration/msbuild-smoke-error/` with a deliberately broken `.gravity` file (e.g. missing `;`) triggers `dotnet build` exit code 1; the build log contains `path(line,col): error PARSE...:` at the MSBuild-canonical format (FR-240).
- **Files.** `tests/integration/msbuild-smoke-error/Smoke.csproj`, `tests/integration/msbuild-smoke-error/registry/broken.gravity`, `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` (extend).
- **Depends on.** T243.

### T245 [P]. Warning test: VAL025 → MSBuild warning (AC-9.4)
- **Acceptance.** A fixture `tests/integration/msbuild-smoke-warning/` declares two chained versions of an entity with a removed transition (triggers VAL025 at Warning severity). `dotnet build` succeeds (exit 0) and the log contains an MSBuild warning at canonical format.
- **Files.** `tests/integration/msbuild-smoke-warning/Smoke.csproj`, `tests/integration/msbuild-smoke-warning/registry/*.gravity`, `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` (extend).
- **Depends on.** T243.

### T245b [P]. Incremental-build test (AC-9.15)
- **Acceptance.** A new fixture `tests/integration/msbuild-smoke-incremental/` reuses the canonical smoke `.gravity` source. The test driver runs `dotnet build /verbosity:minimal` twice back-to-back against the unchanged fixture and scrapes the log: the first invocation MUST contain a `GravityDslGenerate:` execution line; the second invocation MUST NOT (MSBuild's incremental-build short-circuit per FR-208 / T204b skips the target). A third sub-step `touch`es one `.gravity` source between builds and asserts the third invocation re-runs the target in full. The driver uses the same in-process `Process.Start` pattern as the other smoke fixtures; marked `[Trait("Category", "LongRunningTests")]`. Pins AC-9.15.
- **Files.** `tests/integration/msbuild-smoke-incremental/Smoke.csproj`, `tests/integration/msbuild-smoke-incremental/registry/*.gravity`, `Gravity.Dsl.Tests/MsBuild/MsBuildIncrementalBuildTests.cs`.
- **Depends on.** T204b, T243.

### T246. CLI/MSBuild parity test (AC-9.5)
- **Acceptance.** Runs `gravc gen --input samples/registry --output cli-out --emitter csharp` AND `dotnet build` against an equivalent Smoke csproj writing to `msb-out`. SHA-256 hashes both output trees recursively; asserts equality. Marked `[Trait("Category", "Slow")]`. The hashing helper is a shared util — no shell-out.
- **Files.** `Gravity.Dsl.Tests/MsBuild/CliMsBuildParityTests.cs`.
- **Depends on.** T243.

### T247 [P]. AsOf plumbing test (AC-9.9)
- **Acceptance.** A fixture's csproj sets `<GravityDslAsOf>2026-01-01</GravityDslAsOf>`; the `.gravity` source declares `deprecates ... until "2025-12-31"`. `dotnet build` exits non-zero with VAL030 in the MSBuild log. A companion test omits `<GravityDslAsOf>` and confirms today's UTC date is used by default.
- **Files.** `tests/integration/msbuild-smoke-as-of/Smoke.csproj`, `tests/integration/msbuild-smoke-as-of/registry/*.gravity`, `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` (extend).
- **Depends on.** T243.

### T248 [P]. Sample-emitter consumer test (AC-9.8)
- **Acceptance.** `tests/integration/sample-outline-consumer/Smoke.csproj` references both `Gravity.Dsl.MsBuild` and `Gravity.Dsl.Emitter.Sample.Outline` via local-packages. `.gravity.config` enables the `outline` emitter. `dotnet build` produces `.md` files under the configured output dir. Asserts at least one `.md` per entity exists and is non-empty.
- **Files.** `tests/integration/sample-outline-consumer/Smoke.csproj`, `tests/integration/sample-outline-consumer/.gravity.config`, `tests/integration/sample-outline-consumer/registry/*.gravity`, `Gravity.Dsl.Tests/MsBuild/SampleOutlineConsumerTests.cs`.
- **Depends on.** T234, T243.

### T249 [P]. HOST002 ownership test (AC-9.10)
- **Acceptance.** Synthetic test creates two emitter assemblies both claiming `AnnotationNamespace = "outline"` and exposes them to the emitter host via a custom plugin directory. `dotnet build` exits non-zero with `HOST002` in the MSBuild log naming both claimants. This pins FR-052 ownership enforcement through the MSBuild surface.
- **Files.** `Gravity.Dsl.Tests/MsBuild/AnnotationOwnershipMsBuildTests.cs`.
- **Depends on.** T243.

### T250. Item-metadata override test (AC-9.11)
- **Acceptance.** A smoke fixture's csproj declares `<GravityDsl Include="**/*.gravity" Output="custom-out/" />`. `dotnet build` writes generated files under `custom-out/` instead of the default `obj/Generated/`.
- **Files.** `tests/integration/msbuild-smoke-override/Smoke.csproj`, `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` (extend).
- **Depends on.** T243.

### T251. Hook test (AC-9.12)
- **Acceptance.** Asserts the `GravityDslGenerate` target runs before `CoreCompile` and the generated files are picked up by the `<Compile>` item group. Verified by introducing a generated `.cs` file that the consumer's own code references; `dotnet build` succeeds because the generated symbol is available at compile time.
- **Files.** `tests/integration/msbuild-smoke-hook/Smoke.csproj`, `tests/integration/msbuild-smoke-hook/Program.cs` (references a generated type), `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` (extend).
- **Depends on.** T243.

### T252. Empty-input test (AC-9.13)
- **Acceptance.** A consumer csproj with no `.gravity` files (or an empty include) builds successfully with no generated artifacts and no error/warning from `GravityDslGenerate`. The `Condition="'@(GravityDsl)' != ''"` on the target keeps the task from running.
- **Files.** `tests/integration/msbuild-smoke-empty/Smoke.csproj`, `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` (extend).
- **Depends on.** T243.

### T253 [P]. No-global-tool-dependency test (AC-9.14)
- **Acceptance.** Confirms that the MSBuild flow does NOT require `gravc` installed as a global .NET tool — only the `<PackageReference>` to `Gravity.Dsl.MsBuild` is needed. The test runs `dotnet tool list --global` and asserts the resulting line set does NOT contain `gravc` (the test environment must not have a stale `dotnet tool install -g` from an earlier developer workflow). The test then runs the smoke build (`dotnet build tests/integration/msbuild-smoke/MsBuildSmoke.csproj`) and asserts exit code `0` plus the standard generated-artifact set from AC-9.2 / T243. This reframes the previous PATH-scrubbing approach (which was a tautology — the task loads `gravc.dll` from the package's `tasks/net9.0/` regardless of PATH) into a meaningful assertion about distribution model independence.
- **Files.** `Gravity.Dsl.Tests/MsBuild/NoGlobalToolTests.cs`.
- **Depends on.** T243.

### T254. CI workflow update
- **Acceptance.** `.github/workflows/ci.yml` runs the new MsBuild integration tests. The `[Trait("Category", "Slow")]` tests are gated behind a separate matrix leg so the fast lane stays under a minute. Both Linux and macOS legs run the smoke and parity tests (matches the existing T049 / T037 cross-platform coverage).
- **Files.** `.github/workflows/ci.yml`.
- **Depends on.** T243, T246.

---

## Phase gate summary

| Sub-phase | Closing tasks | Spec ACs satisfied |
|---|---|---|
| P9a | T199, T199b, T200–T219 (incl. T204b) | AC-9.1 |
| P9b | T220–T234 | AC-9.6, AC-9.7 |
| P9c | T240–T254 (incl. T245b) | AC-9.2, AC-9.3, AC-9.4, AC-9.5, AC-9.8, AC-9.9, AC-9.10, AC-9.11, AC-9.12, AC-9.13, AC-9.14, AC-9.15 |

Cross-phase notes:
- **AC-9.5 (CLI/MSBuild parity)** is the load-bearing constitutional check (architectural constraint "Build integration parity"). It is closed by T246 with SHA-256 byte-equality, not just exit-code equality, because deterministic output is a Principle I requirement.
- **AC-9.10 (HOST002)** extends the Phase 2 FR-052 annotation-namespace ownership check through the MSBuild surface; the diagnostic format (canonical MSBuild) is the only difference from the CLI-side check.
- **`<GravityDslAsOf>`** is the one MSBuild property that bridges to a Phase 8 CLI flag. The shared `MsBuildDateResolver` (T208) is the single source of truth for date-defaulting on both sides.
- **`BannedSymbolsFile` carve-out** now exempts three projects: `Gravity.Dsl.Cli`, `Gravity.Dsl.Tests`, `Gravity.Dsl.MsBuild`. The list is exact-equality; T202 enforces no prefix matching.

## Revision history

- 2026-05-18 — Initial lock. Phase 9 task plan authored against `spec.md` FR-200..FR-260 and `plan.md` sub-phases P9a / P9b / P9c.
- 2026-05-18 — Critic-pass fixes: public API surface decision LD-13 (`CompilerPipeline` et al. promoted from `internal` to `public` in `Gravity.Dsl.Cli`, locking the CLI helper surface as a stable contract under Principle VI); locked `tasks/net9.0/` everywhere (the build-task NuGet convention) and dropped `tools/net9.0/` references; FR-234 reframed as the NuGet-contents guarantee (`gravc.dll`, not `Gravity.Dsl.Cli.dll`) with the existing parity FR moved to FR-235; `ExcludeAssets="runtime"` mandated for `Microsoft.Build.Utilities.Core` to avoid `AssemblyLoadContext` collisions (T201 updated); per-item `Output`/`Emitter` override pinned to an explicit `Execute()` loop (T206 acceptance updated); `Inputs`/`Outputs` on the target for incremental build introduced as FR-208 / AC-9.15 via new task T204b (target-XML extension) and new test T245b (incremental-build smoke); day-boundary determinism caveat appended to FR-233 documenting that defaulted `<GravityDslAsOf>` can flip diagnostics across midnight; `buildTransitive/`-only packaging (no legacy `build/` mirror) reflected in T203/T204/T205/T212/T213; AC-9.14 reframed from a PATH-scrubbing tautology to an explicit `dotnet tool list --global` assertion (T253 acceptance updated); Roslyn ALC risk added to plan.md §6 risk register with pre-spike T199b gating P9a; CLI helper promotion encoded as new task T199 ahead of T200.
