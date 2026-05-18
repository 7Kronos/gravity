# Gravity DSL — Implementation Plan (Phase 9: Build integration — MSBuild target + reference sample emitter)

**Status:** Locked for implementation
**Date:** 2026-05-18
**Driven by:** `specs/003-phase-9-build-integration/spec.md` and `CLAUDE.md` ("Build integration parity" architectural constraint dominant; Principle VI governs the sample-emitter shape; "Deterministic output" governs packaging).

---

## 1. Strategy

Two scoped sub-phases executed sequentially. P9a delivers the MSBuild target as a self-contained NuGet package that wraps the Phase 0–3 / Phase 8 `CompilerPipeline.Gen` entry point; P9b adds the reference sample emitter as a separate, second NuGet package that demonstrates the `IEmitter` plug-in contract end-to-end. The two halves are decoupled by design: P9a ships even if P9b slips, because the MSBuild target is independently useful with only the bundled `csharp` reference emitter. P9b cannot ship without P9a, because its end-to-end story is the MSBuild-driven consumer fixture under `tests/integration/msbuild-smoke-outline-only/` (AC-9.8).

| Sub-phase | Output | Gate (spec ACs and FRs closed) |
|---|---|---|
| P9a. MSBuild target | New `Gravity.Dsl.MsBuild/` project producing a NuGet package with `buildTransitive/*.props,*.targets`, a `GravityDslGenTask` MSBuild task assembly, and the closed dependency set of `CompilerPipeline.Gen` under `tasks/net9.0/`. Smoke fixture under `tests/integration/msbuild-smoke/`. Incremental-build smoke under `tests/integration/msbuild-smoke-incremental/`. Diagnostic format mapping through `Microsoft.Build.Utilities.Task.Log`. `<GravityDslAsOf>` plumbing. `BannedSymbols.txt` carve-out extended. CLI helpers (`CompilerPipeline`, `PipelineResult`, `DiagnosticFormatter`, `CliRuleIds`) promoted from `internal` to `public` per LD-13. | FR-200, FR-201, FR-202, FR-203, FR-204, FR-205, FR-206, FR-207, FR-208, FR-210, FR-211, FR-212, FR-213, FR-230, FR-231, FR-232, FR-233, FR-234, FR-235, FR-240, FR-241, FR-242, FR-250, FR-251, FR-260. AC-9.1, AC-9.2, AC-9.3, AC-9.4, AC-9.5, AC-9.7-pack, AC-9.9, AC-9.11, AC-9.12, AC-9.13, AC-9.14, AC-9.15. LD-9, LD-11, LD-13. |
| P9b. Sample emitter | New `samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/` project. `OutlineEmitter : IEmitter`. Markdown rendering. Separate NuGet package contributing to a shared `<GravityDslEmitterAssembly>` item. Golden-file suite under `tests/golden/outline/`. Outline-only consumer fixture under `tests/integration/msbuild-smoke-outline-only/`. Cross-claim test for `HOST002` on the MSBuild surface. | FR-220, FR-221, FR-222, FR-223, FR-224, FR-225. AC-9.6, AC-9.7, AC-9.8, AC-9.10. LD-10, LD-12. |

Out-of-scope phases (4–7 reference emitters, the Phase 9b emitter authoring guide, CLI ergonomics polish, Roslyn source generator) are referenced but not addressed; per spec NG-1..NG-10 they remain future work against the Phase 9-shipped MSBuild surface.

## 2. Project layout

Mirrors the predecessor plan; restricted to additions and touches required by Phase 9. No existing AST records, resolver, or validator code are modified — Phase 9 is strictly additive at the build / packaging layer (spec §6 "Cross-references", final paragraph).

```
Gravity.Dsl/
├── Gravity.Dsl.MsBuild/                       # NEW: NuGet package with build assets + MSBuild task
│   ├── buildTransitive/                       # NEW: build assets shipped to direct AND transitive consumers
│   │   ├── Gravity.Dsl.MsBuild.props          # NEW: declares <GravityDsl> item type + default property values
│   │   └── Gravity.Dsl.MsBuild.targets        # NEW: <UsingTask> + GravityDslGenerate target with Inputs/Outputs (FR-208)
│   ├── tasks/GravityDslGenTask.cs             # NEW: Microsoft.Build.Utilities.Task wrapping CompilerPipeline.Gen
│   ├── tasks/MsBuildRuleIds.cs                # NEW: MSB001..MSB010 rule-id constants
│   ├── tasks/MsBuildDateResolver.cs           # NEW: shared DateOnly resolver used by both the CLI and the task (FR-233)
│   └── Gravity.Dsl.MsBuild.csproj             # NEW: <IsPackable>true</IsPackable>, packs buildTransitive/ + tasks/net9.0/
├── samples/emitters/outline/                  # NEW: sample-emitter root (LD-10, LD-12, FR-225)
│   ├── Gravity.Dsl.Emitter.Sample.Outline/
│   │   ├── OutlineEmitter.cs                  # NEW: IEmitter implementation, TargetName="outline"
│   │   ├── OutlineEmitterConfig.cs            # NEW: ConfigurationSchema (one required key: "output")
│   │   ├── Render/EntityOutlineRenderer.cs    # NEW: six-section Markdown renderer
│   │   ├── Render/SectionLabels.cs            # NEW: section heading constants (deterministic order)
│   │   ├── buildTransitive/Gravity.Dsl.Emitter.Sample.Outline.props  # NEW: contributes DLL to <GravityDslEmitterAssembly>
│   │   └── Gravity.Dsl.Emitter.Sample.Outline.csproj                  # NEW: <IsPackable>true</IsPackable>
│   └── README.md                              # NEW: "minimal example, not production" framing per LD-12
├── tests/integration/msbuild-smoke/           # NEW: end-to-end fixture (AC-9.2, AC-9.5)
│   ├── MsBuildSmoke.csproj                    # NEW: <PackageReference Include="Gravity.Dsl.MsBuild" />
│   ├── nuget.config                           # NEW: points at the in-repo local-packages/ feed
│   ├── .gravity.config                        # NEW: csharp + outline emitters enabled
│   └── domain/Employee.gravity                # NEW: re-uses Phase 0–3 canonical sample verbatim
├── tests/integration/msbuild-smoke-broken/    # NEW: parse-error parity fixture (AC-9.3)
│   ├── MsBuildSmokeBroken.csproj
│   ├── nuget.config
│   └── domain/Broken.gravity                  # deliberate PARSE syntax error
├── tests/integration/msbuild-smoke-outline-only/  # NEW: outline-only consumer (AC-9.8)
│   ├── MsBuildSmokeOutline.csproj
│   ├── nuget.config
│   ├── .gravity.config                        # outline emitter only; csharp absent
│   └── domain/Employee.gravity
├── tests/integration/msbuild-smoke-host002/   # NEW: HOST002 cross-claim fixture (AC-9.10)
│   ├── MsBuildSmokeHost002.csproj
│   ├── nuget.config
│   └── plugins/StubOutlineCollidingEmitter.dll  # built from a one-file sibling stub project under tests/stubs/
├── tests/golden/outline/                      # NEW: byte-checked Markdown (AC-9.6, AC-9.7)
│   ├── hr/Employee.md
│   ├── hr/TimeEntry.md
│   └── hr/Project.md
├── Directory.Build.props                      # TOUCH: extend <Choose>/<When> carve-out to exempt Gravity.Dsl.MsBuild
├── BannedSymbols.txt                          # unchanged; the carve-out is project-scoped (see §3.8)
└── Gravity.Dsl.sln                            # TOUCH: add Gravity.Dsl.MsBuild + Gravity.Dsl.Emitter.Sample.Outline
```

The two new NuGet packages are deliberately disjoint: `Gravity.Dsl.MsBuild` carries the build glue and the closed set of compiler/emitter/AST binaries (FR-210); `Gravity.Dsl.Emitter.Sample.Outline` carries only its own assembly plus a `buildTransitive/*.props` that wires its DLL into the shared `<GravityDslEmitterAssembly>` collection consumed by `GravityDslGenerate` (FR-224). The `samples/` ancestor is the in-repo "this is a sample" signal; the `.Sample.` segment in the NuGet id is the equivalent signal for consumers who only see NuGet listings (LD-12 / FR-225).

## 3. Module-level architecture

### 3.1 `Gravity.Dsl.MsBuild.props` (default item type + default properties)

`Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.props` is imported automatically by NuGet at the top of the consuming `.csproj` (NuGet places `buildTransitive/<id>.props` ahead of all other imports for both direct and transitive consumers; the legacy `build/` mirror is deliberately omitted because modern NuGet — 5.0+, every .NET SDK since 3.0 — handles `buildTransitive/` correctly for both consumer classes). It declares the `<GravityDsl>` item type, registers the default item glob, and seeds the four configurable MSBuild properties with their default values. Imports happen before `<Microsoft.NET.Sdk>` items are evaluated so the consumer's csproj can override `<GravityDslDefaultIncludePattern>` and the override takes effect before the default glob fires.

```xml
<Project>

  <PropertyGroup>
    <!-- FR-200: default include glob. Consumers replace by overriding the property
         before this file imports, or by setting <GravityDslDisableDefaultItems>true</…>. -->
    <GravityDslDefaultIncludePattern Condition=" '$(GravityDslDefaultIncludePattern)' == '' ">**/*.gravity</GravityDslDefaultIncludePattern>
    <!-- FR-202 / FR-251: generated artifacts land under $(IntermediateOutputPath)Generated/<emitter>/. -->
    <GravityDslOutputDir Condition=" '$(GravityDslOutputDir)' == '' ">$(IntermediateOutputPath)Generated</GravityDslOutputDir>
    <!-- FR-201: stage to hook the generation target into. Legal: BeforeBuild | BeforeCompile | BeforeResolveReferences. -->
    <GravityDslHook Condition=" '$(GravityDslHook)' == '' ">BeforeBuild</GravityDslHook>
    <!-- FR-200: opt-out for projects with fully-explicit <GravityDsl Include="..." /> lists. -->
    <GravityDslDisableDefaultItems Condition=" '$(GravityDslDisableDefaultItems)' == '' ">false</GravityDslDisableDefaultItems>
    <!-- FR-230: in-proc by default; OutOfProc spawns the bundled gravc executable. -->
    <GravityDslExecMode Condition=" '$(GravityDslExecMode)' == '' ">InProc</GravityDslExecMode>
  </PropertyGroup>

  <!-- Default item group; consumers can extend or replace.
       FR-200: implicit include runs AFTER explicit <GravityDsl Remove="..." /> items so consumers
       can subtract from the default set without disabling it entirely. -->
  <ItemGroup Condition=" '$(GravityDslDisableDefaultItems)' != 'true' ">
    <GravityDsl Include="$(GravityDslDefaultIncludePattern)"
                Condition=" '@(GravityDsl)' == '' " />
  </ItemGroup>

</Project>
```

The `'@(GravityDsl)' == ''` guard means an explicit `<GravityDsl Include="..." />` in the consumer's csproj suppresses the implicit include without requiring `<GravityDslDisableDefaultItems>`. Both opt-out paths are documented in the package README; AC-9.13 pins the explicit-disable path.

### 3.2 `Gravity.Dsl.MsBuild.targets` (the target and the task declaration)

`Gravity.Dsl.MsBuild/buildTransitive/Gravity.Dsl.MsBuild.targets` declares the `GravityDslGenerate` target and the single `<UsingTask>` for the in-package MSBuild task assembly. It is imported by NuGet after `Microsoft.NET.Sdk` so `BeforeBuild` / `BeforeCompile` / `BeforeResolveReferences` hooks are real targets the consumer's build graph already knows about. The target declares MSBuild incremental-build keys (`Inputs="@(GravityDsl)"`, `Outputs="$(GravityDslOutputDir)/.gravity-stamp"`) and ends with a `<Touch>` on the stamp file so back-to-back builds against unchanged sources skip the target entirely (FR-208 / AC-9.15).

```xml
<Project>

  <!-- Resolve the task assembly path inside the package layout. NuGet places this file under
       buildTransitive/, so the task DLL lives in a sibling tasks/net9.0/. -->
  <UsingTask TaskName="Gravity.Dsl.MsBuild.GravityDslGenTask"
             AssemblyFile="$(MSBuildThisFileDirectory)..\tasks\net9.0\Gravity.Dsl.MsBuild.dll" />

  <!-- FR-201: hook selection. The before/after target is computed once into _GravityDslHookTarget;
       MSB006 fires for any other value. -->
  <PropertyGroup>
    <_GravityDslHookTarget Condition=" '$(GravityDslHook)' == 'BeforeBuild' ">BeforeBuild</_GravityDslHookTarget>
    <_GravityDslHookTarget Condition=" '$(GravityDslHook)' == 'BeforeCompile' ">CoreCompile</_GravityDslHookTarget>
    <_GravityDslHookTarget Condition=" '$(GravityDslHook)' == 'BeforeResolveReferences' ">ResolveReferences</_GravityDslHookTarget>
  </PropertyGroup>

  <Target Name="_GravityDslValidateHook"
          BeforeTargets="GravityDslGenerate"
          Condition=" '$(_GravityDslHookTarget)' == '' ">
    <Error Code="MSB006"
           Text="GravityDslHook value '$(GravityDslHook)' is not recognised; expected BeforeBuild, BeforeCompile, or BeforeResolveReferences." />
  </Target>

  <!-- FR-201 / FR-204: generation runs as a single ordered step before the configured hook.
       FR-202: item metadata (Output, Emitter) is passed through to the task.
       FR-208 / AC-9.15: Inputs/Outputs keys make the target incremental — back-to-back
       dotnet build invocations against unchanged sources skip the target entirely. -->
  <Target Name="GravityDslGenerate"
          BeforeTargets="$(_GravityDslHookTarget)"
          Condition=" '@(GravityDsl)' != '' "
          Inputs="@(GravityDsl)"
          Outputs="$(GravityDslOutputDir)\.gravity-stamp">
    <GravityDslGenTask Sources="@(GravityDsl)"
                       OutputDir="$(GravityDslOutputDir)"
                       ConfigFile="$(GravityDslConfig)"
                       AsOf="$(GravityDslAsOf)"
                       ExecMode="$(GravityDslExecMode)"
                       ProjectDirectory="$(MSBuildProjectDirectory)"
                       EmitterAssemblies="@(GravityDslEmitterAssembly)" />
    <!-- FR-208: stamp file written last so MSBuild's Inputs/Outputs comparison can short-circuit
         the next build. AlwaysCreate=true so the file is touched even on no-codegen runs. -->
    <Touch Files="$(GravityDslOutputDir)\.gravity-stamp" AlwaysCreate="true" />
    <!-- FR-260: register the generated .cs tree for the C# compiler that follows. -->
    <ItemGroup>
      <Compile Include="$(GravityDslOutputDir)\csharp\**\*.cs" Exclude="@(Compile)" />
      <FileWrites Include="$(GravityDslOutputDir)\**\*" />
      <FileWrites Include="$(GravityDslOutputDir)\.gravity-stamp" />
    </ItemGroup>
  </Target>

</Project>
```

The `<FileWrites>` registration matters because it lets `dotnet clean` remove the generated tree (including the stamp file); without it, the obj/ directory accumulates stale artifacts across rebuilds and breaks AC-9.5's parity contract on dirty trees. The stamp-file caveat documented in FR-233 still applies: the `Inputs`/`Outputs` mechanism does not detect that the defaulted `<GravityDslAsOf>` crossed a deprecation-window boundary; CI consumers requiring strict date-driven reproducibility MUST pin `<GravityDslAsOf>` explicitly.

### 3.3 `GravityDslGenTask` MSBuild task

`Gravity.Dsl.MsBuild/tasks/GravityDslGenTask.cs` inherits `Microsoft.Build.Utilities.Task` (from the `Microsoft.Build.Utilities.Core` 17.x NuGet package — broadly compatible with every `dotnet` SDK 9.0+). The task surface is intentionally narrow: one input list, four scalar properties, one input metadata-carrying item array. FR-202 mandates per-item `Output` / `Emitter` overrides; the `Execute()` loop groups input items by their resolved override pair and issues one `CompilerPipeline.Gen` invocation per group so item-level metadata actually flows through to the emitter host. Pseudocode for `Execute()`:

```csharp
public sealed class GravityDslGenTask : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();
    [Required] public string OutputDir { get; set; } = string.Empty;
    public string? ConfigFile { get; set; }
    public string? AsOf { get; set; }                     // FR-233 — empty => UtcNow
    public string ExecMode { get; set; } = "InProc";      // FR-230 — InProc | OutOfProc
    [Required] public string ProjectDirectory { get; set; } = string.Empty;
    public ITaskItem[] EmitterAssemblies { get; set; } = Array.Empty<ITaskItem>();  // FR-224

    public override bool Execute()
    {
        // 1. FR-233 — resolve --as-of equivalent. Shared with CLI via MsBuildDateResolver.
        var currentDate = ResolveAsOf();  // T209; on failure logs MSB001 and returns false.
        if (currentDate is null) return false;

        // 2. FR-202 — group Sources by (resolved-Output-override, resolved-Emitter-filter); each
        //    group becomes a separate CompilerPipeline.Gen invocation so item-level metadata
        //    actually applies. When item metadata is absent, the task-level OutputDir / config
        //    block default applies (FR-203).
        var groups = Sources
            .GroupBy(item => (
                Output: item.GetMetadata("Output") ?? OutputDir,
                Emitter: item.GetMetadata("Emitter") ?? ""));

        foreach (var group in groups)
        {
            var sources = group.Select(i => i.ItemSpec).ToList();
            var result = CompilerPipeline.Gen(
                inputs: sources,
                outputRoot: group.Key.Output,
                emitterFilter: string.IsNullOrEmpty(group.Key.Emitter) ? null : group.Key.Emitter,
                configFile: ConfigFile,
                currentDate: currentDate.Value);
            foreach (var d in result.Diagnostics) LogDiagnostic(d);
            if (result.HasErrors) return false;
        }
        return true;
    }
}
```

`CompilerPipeline.Gen` currently scans `inputRoot` for `*.gravity` files; supporting the explicit `IList<string>` of source paths above requires a Phase 9 minor extension to the CLI helper surface — an additive overload of `CompilerPipeline.Gen` accepting `inputs: IList<string>` instead of `inputRoot: string`. The overload ships as a public method per LD-13 / FR-234 and inherits the additive-only stability contract from day one. The existing `inputRoot` overload remains the CLI's default entry point; the MSBuild task uses the new overload exclusively so per-item metadata grouping is honest.

The `ExecMode` dispatch (FR-230) wraps the loop above: `InProc` calls the public `CompilerPipeline.Gen` directly; `OutOfProc` spawns the bundled `gravc` executable from `tasks/net9.0/` once per group with the equivalent `--input`/`--output`/`--emitter`/`--as-of` arguments and pipes stderr/stdout through `LogDiagnostic`. The default (FR-230) is in-proc; out-of-proc is documented escape hatch only (e.g. Roslyn `AssemblyLoadContext` collisions — see §6 risk register). `LogDiagnostic` invokes the canonical `Log.LogError` / `Log.LogWarning` / `Log.LogMessage` mapping in the next subsection so FR-206 / FR-241 hold regardless of which dispatch path ran.

`ForwardDiagnostics` implements FR-206 / FR-241 verbatim:

```csharp
private void ForwardDiagnostics(IReadOnlyList<Diagnostic> diags)
{
    foreach (var d in diags)
    {
        // FR-240: canonical MSBuild form is enforced by the Log API's positional fields.
        switch (d.Severity)
        {
            case DiagnosticSeverity.Error:
                Log.LogError(subcategory: null, errorCode: d.RuleId, helpKeyword: null,
                    file: d.Span.Path, lineNumber: d.Span.Line, columnNumber: d.Span.Column,
                    endLineNumber: 0, endColumnNumber: 0, message: d.Message);
                break;
            case DiagnosticSeverity.Warning:
                Log.LogWarning(subcategory: null, warningCode: d.RuleId, helpKeyword: null,
                    file: d.Span.Path, lineNumber: d.Span.Line, columnNumber: d.Span.Column,
                    endLineNumber: 0, endColumnNumber: 0, message: d.Message);
                break;
            default:
                // FR-241 explicitly accepts the loss of rule-id surface for Info-severity diagnostics.
                Log.LogMessage(MessageImportance.Low, d.RuleId + ": " + d.Message);
                break;
        }
    }
}
```

The four positional `(file, line, col, endLine, endCol)` arguments are exactly what MSBuild's standard console logger and IDE consumers parse to render the canonical `path(line,col): error <ruleId>: <message>` form. No string formatting in the task — letting the Log API render the surface form is what guarantees byte-identical IDE click-through behaviour across Rider, VS, and VS Code.

### 3.4 `MsBuildRuleIds`

`Gravity.Dsl.MsBuild/tasks/MsBuildRuleIds.cs`:

```csharp
internal static class MsBuildRuleIds
{
    public const string Msb001 = "MSB001";   // <GravityDslAsOf> malformed (FR-233 / FR-242)
    public const string Msb002 = "MSB002";   // <GravityDslExecMode> unrecognised (FR-230 / FR-242)
    public const string Msb003 = "MSB003";   // <GravityDslConfig> file does not exist (FR-203 / FR-242)
    public const string Msb004 = "MSB004";   // <GravityDsl> resolved to zero files (FR-242, warning)
    public const string Msb005 = "MSB005";   // Item Output metadata escapes ProjectDirectory (FR-202 / FR-242)
    public const string Msb006 = "MSB006";   // <GravityDslHook> unrecognised (FR-201 / FR-242)
    public const string Msb007 = "MSB007";   // tasks/net9.0/ missing expected assembly (FR-242, defence-in-depth)
    // Msb008..Msb010 reserved per FR-242.
}
```

These constants ship with the task assembly only — they are not added to the compiler library's `RuleIds.cs`. The MSBuild host rule namespace is deliberately separate from the compiler-library rule namespace so a third-party emitter that links the compiler library does not see (or accidentally redefine) `MSB*` ids.

### 3.5 NuGet packaging — `Gravity.Dsl.MsBuild.csproj`

The packaging-relevant csproj properties:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>Gravity.Dsl.MsBuild</PackageId>
    <DevelopmentDependency>true</DevelopmentDependency>           <!-- FR-213 -->
    <IncludeBuildOutput>false</IncludeBuildOutput>                <!-- FR-213; task DLL is staged under tasks/, not lib/ -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>                             <!-- "no lib folder" is intentional -->
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>  <!-- bundle, don't propagate -->
    <Deterministic>true</Deterministic>                           <!-- FR-212 (inherited but stated for clarity) -->
    <EmbedUntrackedSources>true</EmbedUntrackedSources>           <!-- FR-212 -->
    <ContinuousIntegrationBuild Condition=" '$(CI)' == 'true' ">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <ItemGroup>
    <!-- Compile-time references to bring the closure into the build graph and stage it into bin/. -->
    <ProjectReference Include="..\Gravity.Dsl.Compiler\Gravity.Dsl.Compiler.csproj"        PrivateAssets="all" />
    <ProjectReference Include="..\Gravity.Dsl.Ast\Gravity.Dsl.Ast.csproj"                  PrivateAssets="all" />
    <ProjectReference Include="..\Gravity.Dsl.Emitter\Gravity.Dsl.Emitter.csproj"          PrivateAssets="all" />
    <ProjectReference Include="..\Gravity.Dsl.Emitter.CSharp\Gravity.Dsl.Emitter.CSharp.csproj" PrivateAssets="all" />
    <ProjectReference Include="..\Gravity.Dsl.Cli\Gravity.Dsl.Cli.csproj"                  PrivateAssets="all" />
    <!-- Microsoft.Build.Utilities.Core: PrivateAssets=all keeps it out of the consumer's package
         graph; ExcludeAssets=runtime is the load-bearing detail — MSBuild's own AssemblyLoadContext
         provides this assembly, so bundling it under tasks/net9.0/ would produce ALC collision
         warnings. Compile-time visibility is preserved; runtime copies are suppressed. -->
    <PackageReference Include="Microsoft.Build.Utilities.Core"
                      PrivateAssets="all"
                      ExcludeAssets="runtime" />
    <!-- Roslyn workspaces drag in via Gravity.Dsl.Emitter.CSharp. -->
  </ItemGroup>

  <ItemGroup>
    <!-- FR-210: package layout. buildTransitive/ carries the .props/.targets for direct AND
         transitive consumers (no legacy build/ mirror — modern NuGet handles buildTransitive/
         for both consumer classes). tasks/net9.0/ carries the task DLL + the full
         CompilerPipeline closure including gravc.dll. The InProc default loads everything
         from tasks/net9.0/; no separate tools/ directory is shipped. -->
    <None Include="buildTransitive\Gravity.Dsl.MsBuild.props"
          Pack="true" PackagePath="buildTransitive\Gravity.Dsl.MsBuild.props" />
    <None Include="buildTransitive\Gravity.Dsl.MsBuild.targets"
          Pack="true" PackagePath="buildTransitive\Gravity.Dsl.MsBuild.targets" />
    <None Include="$(OutputPath)\**\*.dll" Pack="true" PackagePath="tasks\net9.0\" />
  </ItemGroup>

</Project>
```

The `<ProjectReference … PrivateAssets="all" />` pattern keeps the closure inside this package: consumer projects do not transitively see the compiler/AST/emitter assemblies as ordinary `<Reference>` items in their bin/ output (FR-213). `<SuppressDependenciesWhenPacking>` ensures the produced `.nuspec` declares zero NuGet dependencies — every dependency assembly is bundled under `tasks/net9.0/`, which is the only model that works for an MSBuild task package because the task loads against MSBuild's own `AssemblyLoadContext`, not the consumer's package-resolution graph.

**`Microsoft.Build.Utilities.Core` exclusion.** This is the one dependency that must NOT be bundled. MSBuild's `AssemblyLoadContext` provides its own copy at runtime; shipping a second copy under `tasks/net9.0/` produces ALC collision warnings (the loader sees two assemblies with the same simple name in the task's load context) and in some SDK versions raises a hard load error. The `<PackageReference Include="Microsoft.Build.Utilities.Core" PrivateAssets="all" ExcludeAssets="runtime" />` pattern is the load-bearing pattern: `PrivateAssets="all"` keeps the package out of the consumer's transitive graph (FR-213); `ExcludeAssets="runtime"` keeps the runtime DLL out of the build output so the pack glob `$(OutputPath)\**\*.dll` doesn't include it. AC-9.1's expected-entry list explicitly forbids `Microsoft.Build.Utilities.Core.dll` under `tasks/net9.0/`; a verification step (`unzip -l` on the produced `.nupkg`, or `System.IO.Compression.ZipArchive` enumeration in the pack-content test) MUST confirm its absence.

AC-9.1 verifies the resulting layout via `System.IO.Compression.ZipArchive` enumeration (no shell-out, no `unzip` dependency on CI runners).

### 3.6 Sample emitter — `OutlineEmitter`

`samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/OutlineEmitter.cs` mirrors the `CSharpEmitter` shape (FR-220). It is `public sealed class OutlineEmitter : IEmitter` and exposes:

| Member | Value | Source |
|---|---|---|
| `TargetName` | `"outline"` | LD-12 / FR-220 |
| `AnnotationNamespace` | `"outline"` | LD-12 / FR-220 (claimed exclusively; HOST002 enforces uniqueness) |
| `SupportedAstVersions` | `SemanticVersionRange.Parse(">=1.0.0 <2.0.0")` | FR-220 (matches the C# reference emitter so Phase 8's AST 1.1.0 bump does not break it) |
| `ConfigurationSchema` | one key `output: string, required, no default` | FR-223 |

The `Emit` method walks `model.Declarations` in `(FQN ordinal, Version ascending)` order — the same iteration the C# emitter uses (FR-221 inherits the FR-161 order). For each `EntityDecl` it produces one Markdown file. `ValueTypeDecl` and `EnumDecl` are deliberately ignored (FR-221 second sentence — keeping the surface small reinforces "sample, not reference"):

```csharp
public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
{
    if (model is null) throw new ArgumentNullException(nameof(model));
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (sink is null) throw new ArgumentNullException(nameof(sink));

    var declToFile = BuildDeclToFile(model);  // identical helper to CSharpEmitter.BuildDeclToFile

    foreach (var kv in model.Declarations)
    {
        if (kv.Value is not EntityDecl entity) continue;       // FR-221: entities only
        var file = declToFile[kv.Key.Fqn];
        string? dslNs = file.Namespace?.Name;
        string dir = NamespaceMapper.ComposeDirectory(dslNs);  // same helper used by CSharpEmitter
        string sourceRel = Path.GetFileName(file.Path);        // header-only; FR-222 forbids working-dir paths
        string body = EntityOutlineRenderer.Render(entity, kv.Key.Version, sourceRel);
        sink.WriteFile(Path.Combine(dir, entity.Name + ".md").Replace('\\', '/'), body);
    }

    return new EmitResult(ImmutableArray<Diagnostic>.Empty);
}
```

The directory layout per entity is `outline/<namespace-path>/<EntityName>.md` — composed via `NamespaceMapper.ComposeDirectory`, the same Phase 0–3 helper the C# emitter uses, so cross-emitter consumers get directory parity. The `outline/` prefix is the `output:` config key value; the host writes into `<outputRoot>/<output>/...`. The samples emitter does not need its own `NamespaceMapper` — `Gravity.Dsl.Emitter.CSharp.NamespaceMapper` is `public` and is the canonical helper; the sample csproj `<PackageReference>`s `Gravity.Dsl.Emitter.CSharp` to consume it. (This is one of the documented re-use patterns for plug-in emitters.)

### 3.7 `EntityOutlineRenderer` — Markdown surface

`samples/emitters/outline/Gravity.Dsl.Emitter.Sample.Outline/Render/EntityOutlineRenderer.cs`:

```csharp
internal static class EntityOutlineRenderer
{
    private const string Lf = "\n";  // FR-222 — constitution determinism standard

    public static string Render(EntityDecl entity, int version, string sourceFileName)
    {
        var sb = new StringBuilder();
        // Single deterministic header line per FR-222.
        sb.Append("<!-- generated from ").Append(sourceFileName).Append(" -->").Append(Lf);
        sb.Append(Lf);
        // H1: entity name + @version
        sb.Append("# ").Append(entity.Name).Append("@")
          .Append(version.ToString(CultureInfo.InvariantCulture)).Append(Lf);
        sb.Append(Lf);
        // The six sections, in fixed order (FR-221).
        AppendIdentity(sb, entity);
        AppendRelations(sb, entity);
        AppendProperties(sb, entity);
        AppendLifecycle(sb, entity);
        AppendEvents(sb, entity);
        AppendCommands(sb, entity);
        return sb.ToString();
    }
    // ...
}
```

The six section helpers each emit `## <Heading>` followed by either a table / sub-tree of content **or** the canonical empty marker `_(none)_` (FR-221 third sentence). Format details per section:

- `AppendIdentity`: H2 `## Identity` + a single field line `- **<name>**: <type>` (entity identity is exactly one field in v1 grammar).
- `AppendRelations`: H2 `## Relations` + one bullet per relation: `- **<name>**: <Target> [<cardinality>]`. Cardinality renders as `1`, `0..1`, `0..*`, `1..*` matching the grammar surface forms.
- `AppendProperties`: H2 `## Properties` + one bullet per property: `- **<name>**: <Type>` where `<Type>` is `SourceWriter.WriteTypeRef`-equivalent (re-uses the Phase 8 `@N` rendering for free).
- `AppendLifecycle`: H2 `## Lifecycle` + sub-sections `### States` (one bullet per state) and `### Transitions` (one bullet per transition `- <from> -> <to>` with optional guard suffix).
- `AppendEvents`: H2 `## Events` + one sub-section per event: `### <EventName>` followed by a Markdown table `| Field | Type |` for the payload.
- `AppendCommands`: H2 `## Commands` + one sub-section per command: `### <CommandName>` + arguments table + returns line + side-effect line (`Emits: <EventName>`).

Iteration inside each section follows declaration order from the AST (FR-222 second sentence — the AST node order is already deterministic from Phase 0–3). No sorting is applied inside sections; sorting would distort author intent. Determinism comes from input ordering, not from output re-sorting.

### 3.8 Build-integration parity and `<GravityDslAsOf>` plumbing

The task assembly references `Gravity.Dsl.Compiler`, `Gravity.Dsl.Ast`, `Gravity.Dsl.Emitter`, `Gravity.Dsl.Emitter.CSharp`, and `Gravity.Dsl.Cli` (all `PrivateAssets="all"`). The in-proc path invokes `CompilerPipeline.Gen` directly — the same library entry the CLI's `RunGen` calls (`Program.cs:65-66`). Identical diagnostic objects flow from the compiler; only the formatter differs (MSBuild's `Log` API positional parameters in the task; `DiagnosticFormatter.Format` in the CLI's `PrintDiagnostics`). FR-230 / FR-232 are therefore enforced by **shared code**, not by parallel re-implementation.

`<GravityDslAsOf>` flows through a small shared helper `MsBuildDateResolver.TryResolve(string? raw, out DateOnly asOf, out string? error)`. This helper is **literally the same code** that lives in `Gravity.Dsl.Cli/Program.cs:TryResolveAsOf` (lines 79-95) — pulled out into a new `Gravity.Dsl.Cli.MsBuildDateResolver` internal static class and exposed to the MsBuild project via `[InternalsVisibleTo("Gravity.Dsl.MsBuild")]` on the CLI assembly. The CLI's `Program.cs:TryResolveAsOf` becomes a one-line forwarder to the shared helper. This pins FR-233 / FR-141 / LD-7's "single clock read" invariant: the helper has exactly one `DateTime.UtcNow` call site and exactly two consumers (the CLI's `RunCheck` / `RunGen` and the MSBuild task's `Execute`).

The `BannedSymbols.txt` carve-out (`Directory.Build.props` lines 22-32) is extended to exempt `Gravity.Dsl.MsBuild` as well as `Gravity.Dsl.Cli` and `Gravity.Dsl.Tests`. The exempt set becomes a closed list of three project names, encoded as an exact-equality MSBuild condition so a future `Gravity.Dsl.MsBuild.Anything` project cannot accidentally inherit the exemption:

```xml
<Choose>
  <When Condition=" '$(MSBuildProjectName)' != 'Gravity.Dsl.Tests'
                AND '$(MSBuildProjectName)' != 'Gravity.Dsl.Cli'
                AND '$(MSBuildProjectName)' != 'Gravity.Dsl.MsBuild' ">
    <ItemGroup>
      <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers"> ... </PackageReference>
      <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
    </ItemGroup>
  </When>
</Choose>
```

A comment block above the `<Choose>` enumerates the three exempt projects and the FR / LD ids that justify each exemption, so the rationale is on-screen for any future reader of `Directory.Build.props`.

## 4. Determinism strategy

Phase 9 introduces a new packaging artifact (the `.nupkg`) and a new task-assembly entry point. Three concrete commitments preserve the project-wide byte-identical-across-runs guarantee:

- **(a) Markdown rendering is byte-deterministic.** `EntityOutlineRenderer` uses `"\n"` line endings (constitution determinism standard), `int.ToString(CultureInfo.InvariantCulture)` for any integer rendering, and iteration order taken from AST node order (which is already Phase 0–3 / Phase 8 deterministic). No `DateTime`, no machine name, no absolute paths appear in the output — the source-file reference in the header is `Path.GetFileName(...)` only, matching the Phase 0–3 `CSharpEmitter.GravityRelativePath` convention (lines 129-140). The `output:` config key is the only consumer-supplied string that affects directory layout, and it is consumer-supplied by design. AC-9.7 pins this with a same-process-twice golden assertion.
- **(b) NuGet pack is deterministic.** `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>`, and `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` (already CI-conditioned by `Directory.Build.props:12`) suppress timestamps in PE headers and normalise the embedded source paths. NuGet's pack pipeline sorts archive entries by path ordinally; verified by AC-9.7-pack via repeated `dotnet pack` + `sha256sum` of the resulting `.nupkg`. No host-specific paths leak into the `.nuspec` because `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>` short-circuits the project-reference-to-package-dependency normalisation pass that would otherwise embed the host's package-cache layout.
- **(c) MSBuild Task respects the same banned-APIs constraints as the CLI.** Exactly one `DateTime.UtcNow` read in the entire P9a deliverable, and it lives inside `Gravity.Dsl.Cli.MsBuildDateResolver.TryResolve` (called by both the CLI and the MSBuild task). No other clock reads, no `Environment.MachineName`, no `Stopwatch`-based path. Determinism preconditions inherited from Phase 0–3 (file iteration in ordinal order, `ImmutableSortedDictionary` everywhere) flow through unchanged because the MSBuild task is a thin wrapper over `CompilerPipeline.Gen`.

## 5. Test strategy

Three tiers mirroring the predecessor plan: a sample-emitter golden suite, an MSBuild integration smoke suite, and a CLI/MSBuild parity suite. All tiers run on every PR.

### 5.1 Sample emitter golden-file tests (AC-9.6, AC-9.7)

`tests/golden/outline/` contains byte-checked Markdown for the three canonical entities `Employee.md`, `TimeEntry.md`, `Project.md` rendered from the existing `samples/registry/` inputs. `Gravity.Dsl.Tests/Emitter/Outline/OutlineGoldenFileTests.cs` mirrors `Gravity.Dsl.Tests/Emitter/CSharp/GoldenFileTests.cs` line-for-line — same fixture-loading pattern, same per-file byte-equality assertion, same "update goldens" command-line knob via the `GRAVITY_UPDATE_GOLDENS=1` environment variable already wired in Phase 0–3.

Two additional assertions beyond the C# golden suite:

1. **Six-section coverage.** Every emitted `.md` contains exactly the headings `## Identity`, `## Relations`, `## Properties`, `## Lifecycle`, `## Events`, `## Commands` in that order (AC-9.6). Pinned by a simple regex match on the rendered string before the byte comparison runs.
2. **Same-process determinism.** A single test runs `OutlineEmitter.Emit` twice against the same `ResolvedModel`, hashes both buffer trees, asserts equality (AC-9.7 first half). The second half — cross-OS-image determinism — falls out of CI running on both Linux and Windows runners against the same goldens; no test code is needed for it.

### 5.2 MSBuild integration smoke tests (AC-9.2, AC-9.3, AC-9.4, AC-9.11, AC-9.12, AC-9.13)

`Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` is an xUnit driver that:

1. Locates the repo root via `Directory.GetParent(...)` (avoids the test runner's working-directory drift).
2. `dotnet pack`s `Gravity.Dsl.MsBuild.csproj` and `Gravity.Dsl.Emitter.Sample.Outline.csproj` into `tests/integration/local-packages/` (one shared directory across all smoke fixtures; created on first test, gated by an xUnit `[CollectionDefinition]` so it runs once per test session).
3. For each fixture under `tests/integration/msbuild-smoke*/`, runs `dotnet build <fixture>/<csproj> /p:Configuration=Release`.
4. Asserts:
   - **AC-9.2.** Exit code `0`; `obj/Generated/csharp/Employee.cs` exists and is non-empty; the same build's `bin/<config>/<tfm>/MsBuildSmoke.dll` exists (proving the generated C# compiled).
   - **AC-9.3.** Exit code non-zero; stderr+stdout contain exactly one error line matching the regex `^.*Broken\.gravity\(\d+,\d+\): error PARSE\d{3}:`.
   - **AC-9.4.** Exit code `0`; stdout contains exactly one warning line matching `^.*\.gravity\(\d+,\d+\): warning VAL025:`; a second invocation with `/warnaserror:VAL025` flips the exit code to non-zero (cross-pins the `warningCode` field is being filled per FR-241).
   - **AC-9.11.** Item-metadata override fixture writes to `custom/out/` (resolved relative to `$(MSBuildProjectDirectory)`); `obj/Generated/outline/` is empty.
   - **AC-9.12.** `<GravityDslHook>BeforeCompile</GravityDslHook>` fixture builds successfully; a second fixture with `<GravityDslHook>Garbage</GravityDslHook>` fails with `MSB006` in the log.
   - **AC-9.13.** `<GravityDslDisableDefaultItems>true</GravityDslDisableDefaultItems>` fixture with zero `<GravityDsl>` items builds successfully and produces no `obj/Generated/**` tree.

The driver uses `System.Diagnostics.Process` with a captured stdout/stderr pair and a 90-second timeout per `dotnet build` invocation. The xUnit collection is tagged `LongRunningTests` so it can be skipped during fast local iteration (`dotnet test --filter "Category!=LongRunningTests"`).

### 5.3 CLI/MSBuild parity test (AC-9.5, FR-234)

`Gravity.Dsl.Tests/MsBuild/CliMsBuildParityTests.cs`:

```
1. Resolve a deterministic temp directory under tests/tmp/parity-<guid>/.
2. Run `gravc gen --input samples/registry --output cli-out
                  --emitter csharp --emitter outline --as-of 2026-05-18`.
3. Run `dotnet build tests/integration/msbuild-smoke/MsBuildSmoke.csproj
                       /p:GravityDslAsOf=2026-05-18 /p:GravityDslOutputDir=<temp>/msb-out`.
4. Walk both output trees; collect `(relative-path, sha256(content-bytes))` tuples.
5. Sort the tuple lists; assert ordinal equality.
```

Per FR-234 there is no allowlist of expected differences; any divergence fails the test. The `samples/registry/` input is the canonical reference corpus and includes Phase 8's `Employee@1 + Employee@2 deprecates` chain so the parity contract exercises versioning end-to-end. AC-9.9 (the `<GravityDslAsOf>` plumbing) is verified by a sibling test that varies the `--as-of` / `/p:GravityDslAsOf=` value across three rows (`2027-01-01`, `2026-12-31`, `2026-13-45`) and asserts the documented diagnostic outcomes for each.

### 5.4 NuGet pack content + determinism test (AC-9.1, AC-9.7-pack)

`Gravity.Dsl.Tests/MsBuild/NuGetPackContentTests.cs`:

```
1. `dotnet pack Gravity.Dsl.MsBuild.csproj -c Release -o <temp>/pack-1/`.
2. `dotnet pack Gravity.Dsl.MsBuild.csproj -c Release -o <temp>/pack-2/`.
3. Assert sha256(pack-1/*.nupkg) == sha256(pack-2/*.nupkg).       // AC-9.7-pack
4. Open pack-1/*.nupkg via ZipArchive.
5. Assert the entry set is exactly:
     buildTransitive/Gravity.Dsl.MsBuild.props
     buildTransitive/Gravity.Dsl.MsBuild.targets
     tasks/net9.0/Gravity.Dsl.MsBuild.dll
     tasks/net9.0/Gravity.Dsl.Compiler.dll
     tasks/net9.0/Gravity.Dsl.Ast.dll
     tasks/net9.0/Gravity.Dsl.Emitter.dll
     tasks/net9.0/Gravity.Dsl.Emitter.CSharp.dll
     tasks/net9.0/gravc.dll                       (CLI assembly; FR-234)
     tasks/net9.0/Pidgin.dll
     tasks/net9.0/YamlDotNet.dll
     tasks/net9.0/Microsoft.CodeAnalysis.*.dll    (Roslyn closure)
     Gravity.Dsl.MsBuild.nuspec
     [Content_Types].xml
     _rels/.rels
     package/services/metadata/core-properties/<guid>.psmdcp
   Assert ALSO that the entry set does NOT contain
   `tasks/net9.0/Microsoft.Build.Utilities.Core.dll` — that assembly is provided
   by MSBuild's own ALC and bundling it triggers collision warnings (FR-210, AC-9.1).
6. Repeat (1)-(5) for Gravity.Dsl.Emitter.Sample.Outline.csproj with the trimmed
   layout (buildTransitive/*.props + tasks/net9.0/Gravity.Dsl.Emitter.Sample.Outline.dll only).
```

Step 5 uses `System.IO.Compression.ZipArchive` enumeration — no shell-out, no `unzip` dependency on CI runners (the smoke runner image already has the .NET SDK but may not have `unzip` on Windows).

### 5.5 Sample emitter end-to-end consumer test (AC-9.8)

`tests/integration/msbuild-smoke-outline-only/MsBuildSmokeOutline.csproj` declares both `<PackageReference Include="Gravity.Dsl.MsBuild" />` and `<PackageReference Include="Gravity.Dsl.Emitter.Sample.Outline" />`. Its `.gravity.config` lists only the `outline:` block (no `csharp:`). The smoke driver builds it and asserts (a) exit code `0`, (b) `obj/Generated/outline/Employee.md` exists, (c) `obj/Generated/csharp/` does not exist (the `csharp` emitter is registered but disabled by config absence — the existing `Gravity.Dsl.Emitter.ConfigLoader` semantics carry through unchanged).

### 5.6 Annotation-namespace collision through the MSBuild surface (AC-9.10)

`tests/integration/msbuild-smoke-host002/` carries a stub colliding emitter assembly built from `tests/stubs/StubOutlineCollidingEmitter/` — a one-file `IEmitter` whose `TargetName` is `"outline-collider"` and `AnnotationNamespace` is `"outline"`. The fixture's csproj loads both the sample outline emitter (via the sample's package) and the stub (via a literal `<GravityDslEmitterAssembly Include="plugins/StubOutlineCollidingEmitter.dll" />` in the csproj). The smoke driver builds it and asserts non-zero exit + a `HOST002` line in the MSBuild log naming both `outline` and `outline-collider` claimants. No generated output is produced (the existing host pre-flight aborts before any emitter runs — `EmitterHost.cs:107-136`).

### 5.7 No-global-tool-dependency test (AC-9.14)

The test runs `dotnet tool list --global` and asserts the resulting line set does NOT contain `gravc`. The test then runs the smoke build (`dotnet build tests/integration/msbuild-smoke/MsBuildSmoke.csproj`) and asserts exit code `0` plus the standard generated-artifact set from AC-9.2. This validates the `<PackageReference>`-only distribution model (LD-9) without relying on PATH-scrubbing tautologies — the in-proc task loads `gravc.dll` from the package's `tasks/net9.0/`, so the absence of a global `gravc` tool is what proves the package is self-sufficient.

## 6. Risk register (Phase 9 surface)

| Risk | Surface in Phase 9 | Mitigation |
|---|---|---|
| MSBuild SDK version drift. | The task assembly compiled against a specific `Microsoft.Build.Utilities.Core` version may not load on older `dotnet` SDKs. | Pin `Microsoft.Build.Utilities.Core` to the 17.x line (broadly compatible with every SDK 9.0+); document the minimum SDK version in the package README and in `global.json` (already pinned at `9.0.314`). CI matrix runs the smoke suite against `dotnet --version` rows `9.0.314` (pinned) and `9.0-latest` to catch drift early. |
| Roslyn (`Microsoft.CodeAnalysis.CSharp.Workspaces`) in MSBuild Task ALC. | `Gravity.Dsl.Emitter.CSharp` pulls in Roslyn; MSBuild loads its own (older) Roslyn copy; version collisions in the Task ALC are a known minefield. | Smoke test at T243 runs `dotnet build` against the integration fixture as the very first implementation gate. If a Roslyn collision surfaces, options are: (a) wrap task entry in a fresh `AssemblyLoadContext` (`MSBuildLoadContext` pattern from SDK), (b) flip default to OutOfProc invocation (spawn `gravc` via `<Exec>` task). Phase 9 ships InProc as the documented default; the OutOfProc escape hatch is a hot-swap if smoke fails. The gating spike T199b runs **before** any further P9a implementation work continues. |
| Task assembly dependency closure too large. | Roslyn (`Microsoft.CodeAnalysis.CSharp.Workspaces`) transitive deps make the `.nupkg` heavy (several MB), which slows `dotnet restore` for consumers. | Use `<PackageReference … PrivateAssets="all" />` on every `<ProjectReference>` so the consumer's package graph does not transitively see the closure; report `.nupkg` size in CI as a tracked metric (fails the build if it grows by >25% release-over-release without a documented justification in the PR description). |
| `buildTransitive/` vs `build/` confusion. | Older NuGet versions read `build/` only; modern NuGet expects `buildTransitive/` so an indirect consumer (consumer ➜ domain library ➜ Gravity.Dsl.MsBuild) still picks up the import. Phase 9 ships `buildTransitive/` only — modern NuGet (5.0+, every .NET SDK since 3.0) handles `buildTransitive/` for both direct and transitive consumers. | Ship `buildTransitive/<id>.props,targets` only; do NOT populate `build/`. AC-9.1's expected-entry list reflects this single path. If a future need to support pre-3.0 SDKs emerges, the `build/` mirror can be re-added without breaking the `buildTransitive/` path; until then, mirroring is dead weight. |
| Deterministic pack difficulty. | NuGet pack normalisation is sensitive to file timestamps, working-directory absolute paths embedded in the `.nuspec`, and the ordering of `_rels/.rels` entries. | `<Deterministic>true</Deterministic>` + `<EmbedUntrackedSources>true</EmbedUntrackedSources>` + `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` + `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>`. AC-9.7-pack runs `dotnet pack` twice and `sha256sum`s the result; the test fails the build on any byte drift. |
| Smoke test brittleness. | Spawning `dotnet build` in a test is slow (multi-second per fixture) and CI-fragile (NuGet feed races, transient HTTP failures on `Microsoft.Build.Utilities.Core` restore). | All smoke tests are tagged `[Trait("Category", "LongRunningTests")]` with a 90-second per-invocation timeout. The local-packages feed is built once per test session via an xUnit `[CollectionDefinition]` and shared across fixtures (`tests/integration/local-packages/`). CI caches `~/.nuget/packages/` keyed on `Directory.Packages.props` hash so cold-restore happens at most once per package version change. |
| Sample emitter sets community precedent. | If the sample is poor, every community emitter inherits its idioms — bad naming conventions, missing config validation, sloppy determinism. | Mirror `CSharpEmitter`'s code organisation exactly (same `EmitOne` helper shape, same `BuildDeclToFile` helper, same `NamespaceMapper` usage). Include `samples/emitters/outline/README.md` whose top three sentences are "This is a minimal example, not a production target. It exists to demonstrate the `IEmitter` contract. Production emitters belong under the project root next to `Gravity.Dsl.Emitter.CSharp/`." LD-12 frames this as a permanent commitment. |
| AsOf flag plumbing inconsistency. | The MSBuild property `<GravityDslAsOf>` and the CLI flag `--as-of` could drift in their default-resolution semantics (e.g. CLI uses UtcNow but MSBuild uses local time, or vice versa). | One shared default-resolution helper (`Gravity.Dsl.Cli.MsBuildDateResolver.TryResolve`) consumed by both `Program.cs:TryResolveAsOf` and `GravityDslGenTask.Execute`. The helper has exactly one `DateTime.UtcNow` call site; the CLI delegates to it via a one-line forwarder. A unit test asserts that the two consumers agree byte-for-byte across a parameterised matrix of inputs (null, empty, valid date, malformed date). |
| Banned-API carve-out scope creep. | Extending the `Directory.Build.props` `<Choose>/<When>` exempt list could accidentally exempt a future `Gravity.Dsl.MsBuild.X` project that shouldn't be reading the clock. | Use exact-equality conditions (`'$(MSBuildProjectName)' != 'Gravity.Dsl.MsBuild'`), not substring/prefix matches. Document the closed exempt set (`Gravity.Dsl.Cli`, `Gravity.Dsl.Tests`, `Gravity.Dsl.MsBuild`) in a comment block above the `<Choose>`, with FR / LD id traces for each row. A CI lint asserts that the exempt set has exactly three entries and matches the documented list verbatim. |

### 6.1 Spike result (T199b, recorded 2026-05-18)

**InProc loaded cleanly. No Roslyn ALC collision. InProc default holds.** `dotnet pack` produced a 16-MB `.nupkg` with `gravc.dll` + every transitive runtime assembly (`Pidgin`, `YamlDotNet`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `System.Composition.*`, `Humanizer`) under `tasks/net9.0/`. A minimal consumer csproj referencing the local-packages feed successfully invoked `GravityDslGenerate` via `BeforeTargets="CoreCompile"`, ran `CompilerPipeline.Gen` end-to-end, generated `.cs` artefacts, and compiled them as part of the consumer build. Zero `FileLoadException`, `MissingMethodException`, ALC warnings, or Roslyn version-mismatch errors observed. Two unrelated CS8669 warnings on generated files (nullable annotation context — separately tracked; not an ALC issue). Two pack-pipeline fixes were required to make this work and are now locked into `Gravity.Dsl.MsBuild.csproj`: `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` (forces transitive runtime deps into build output so the pack glob sweeps them) and `<TargetsForTfmSpecificContentInPackage>...PackTaskFiles</TargetsForTfmSpecificContentInPackage>` with `@(TfmSpecificPackageFile)` (the supported NuGet pack hook; the older `<None Pack="true">` pattern does not survive `IncludeBuildOutput=false`). The OutOfProc escape hatch is deferred indefinitely.

## 7. Out-of-scope acknowledgements

Documented here so they do not creep in. Each item maps to a spec non-goal (NG-1..NG-10) or is carried over from earlier phases. Phase 9 is the build-integration layer; future phases extend the emitter set and the authoring story.

- **Emitter authoring guide** (NG-1). The end-user-facing tutorial document is a separately-scoped Phase 9b deliverable. Phase 9 ships only the sample emitter source; community authors read its source and the existing `IEmitter` XML doc until 9b lands.
- **CLI ergonomics polish** (NG-2). `--watch`, `--verbose`, coloured output, progress reporting, summary tables. Out of scope; the CLI is not modified by Phase 9 except for the one-line forwarder change in `TryResolveAsOf` to delegate to `MsBuildDateResolver`.
- **Roslyn source generator** (NG-3). Deferred per `docs/specs.md` §9. The MSBuild target running as a `BeforeBuild` step is the explicit plan for v1; the source-generator architecture is a separate (and larger) problem.
- **Watch mode / live regeneration** (NG-4). No file-system watcher in the task; generation happens once per `dotnet build` invocation. Future work could add `dotnet watch` integration but is not Phase 9.
- **IDE / project-system hooks** (NG-5). No special integration with VS / Rider / VS Code beyond what MSBuild itself provides through its standard logger; the canonical `path(line,col): error <ruleId>: <message>` format is what IDEs already parse natively.
- **Per-build telemetry** (NG-6). No phone-home, no opt-in usage stats, no build-time pings. The task assembly contains zero HTTP client code.
- **Full deterministic-output hardening beyond Phase 0–3 / Phase 8** (NG-7). The constitution-level determinism bar is already met by Phase 0–3 emitters (AC-6a / AC-6b) and Phase 8 ordering rules. Phase 9 inherits that bar; it does not add new determinism guarantees beyond `.nupkg` packaging (AC-9.7-pack).
- **Error-reporting polish** (NG-8). Beyond the format mapping in FR-240 / FR-241, the diagnostic content (rule id, message, span) is unchanged from Phase 0–3 / Phase 8. Re-wordings, fix hints, structured codes, MSBuild help-link URLs are deferred.
- **Phases 4–7 reference emitters** (NG-9). JSON Schema, GraphQL, OpenAPI, AsyncAPI remain TBD. The outline sample does **not** substitute for any of them.
- **All Phase 0–3 and Phase 8 out-of-scope discipline carries forward** (NG-10): scopes / permissions / rules / releases / library imports (Registry concerns per Principle VII); storage backends and projection layouts; runtime topology; broker configuration; AI authoring tooling; LSP, formatter, linter; runtime DSL evaluation; cross-compile diffing via persisted manifest; per-field `@deprecated`; auto-migration tooling. Not addressed in Phase 9.

## 8. Revision history

- 2026-05-18 — Initial lock. Phase 9 implementation plan authored against `spec.md` (Phase 9 narrowed slice). Two sub-phases sequenced (P9a MSBuild target, P9b sample emitter); project layout, module-level architecture, determinism strategy, test strategy, risk register, and out-of-scope acknowledgements documented.
- 2026-05-18 — Critic-pass fixes: public API surface decision LD-13 (`CompilerPipeline` et al. promoted from `internal` to `public` in `Gravity.Dsl.Cli`, locking the CLI helper surface as a stable contract under Principle VI); locked `tasks/net9.0/` everywhere (the build-task NuGet convention) and dropped `tools/net9.0/` references; FR-234 reframed as the NuGet-contents guarantee (`gravc.dll`, not `Gravity.Dsl.Cli.dll`) with the existing parity FR moved to FR-235; `ExcludeAssets="runtime"` mandated for `Microsoft.Build.Utilities.Core` to avoid `AssemblyLoadContext` collisions; per-item `Output`/`Emitter` override pinned to an explicit `Execute()` loop (§3.3 pseudocode); `Inputs`/`Outputs` on the target for incremental build introduced as FR-208 / AC-9.15 with `<Touch>` stamp-file pattern in §3.2; day-boundary determinism caveat appended to FR-233 documenting that defaulted `<GravityDslAsOf>` can flip diagnostics across midnight; `buildTransitive/`-only packaging (no legacy `build/` mirror) reflected in §3.1, §3.2, §3.5, and the risk-register row; AC-9.14 reframed from a PATH-scrubbing tautology to an explicit `dotnet tool list --global` assertion; Roslyn ALC risk added to §6 risk register with pre-spike T199b gating P9a; CLI helper promotion encoded as new task T199 ahead of T200.
