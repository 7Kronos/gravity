# Gravity DSL Playground — multi-emitter showcase

A self-contained .NET solution that pulls Gravity DSL into a normal `dotnet
build` and runs **three emitters** (C#, JSON Schema, PostgreSQL DDL) at
compile time. One `.gravity` source file becomes a compiled library plus a
folder of JSON schemas plus a folder of SQL DDL — with no hand-authored
`.cs`, `.json`, or `.sql` in sight.

The goal is not to demo the CLI (`gravc`) — that's already covered by the
integration harness. It's to show what consumers see: add a
`<PackageReference>` per emitter, drop a `.gravity` file in the project,
list the emitters in `.gravity.yaml`, and the build does the rest.

## Layout

```
Playground/
├── Playground.sln                          ← top-level solution
├── Directory.Build.props                   ← re-imports the repo's root props
├── Directory.Packages.props                ← inherits CPM + adds local-feed version pin
├── NuGet.config                            ← restricts Gravity.Dsl.* to local-packages/
├── scripts/
│   └── pack.sh                             ← packs every Gravity.Dsl.* NuGet into local-packages/
├── local-packages/                         ← local NuGet feed (gitignored .nupkg)
├── Gravity.Playground.HrDemo/              ← the actual consumer project
│   ├── Gravity.Playground.HrDemo.csproj    ← <PackageReference> per emitter + Gravity.Dsl.MsBuild
│   ├── .gravity.yaml                       ← emitter config (one block per emitter)
│   └── Hr.gravity                          ← the .gravity source
└── golden/csharp/hr/                       ← frozen reference output (checked in)
    ├── Employee.cs
    ├── EmployeeState.cs
    ├── EmployeeEvents.cs
    ├── EmployeeCommands.cs
    ├── ContactInfo.cs
    ├── ContractType.cs
    ├── OnboardResult.cs
    └── TerminationResult.cs
```

## Run it

```bash
# One-time (or whenever Gravity.Dsl.MsBuild changes): pack the local feed.
./scripts/pack.sh

# Build the playground.
dotnet build Playground.sln
```

`dotnet build` will:

1. Restore every `Gravity.Dsl.*` package from `local-packages/`.
2. Auto-import each emitter package's `buildTransitive/<PackageId>.props`,
   which contributes the emitter DLL to `<GravityDslEmitterAssembly>`.
3. Auto-import `buildTransitive/Gravity.Dsl.MsBuild.props` + `.targets`.
4. Run the `GravityDslGenerate` target before `CoreCompile`.
5. The target reads `Hr.gravity` + `.gravity.yaml`, invokes every emitter
   named in the config, and writes:
   - `obj/Debug/net9.0/Generated/csharp/hr/*.cs` (C# emitter)
   - `obj/Debug/net9.0/Generated/json-schema/**/*.json` (JSON Schema emitter)
   - `obj/Debug/net9.0/Generated/postgres-ddl/**/*.sql` (PostgreSQL DDL emitter)
6. The target adds the `.cs` files to `<Compile>` so they're compiled into
   `bin/Debug/net9.0/Gravity.Playground.HrDemo.dll`. Non-`.cs` outputs are
   left on disk for downstream tooling (the `.json` and `.sql` files are
   not auto-included anywhere — picking them up is the consumer's job).

## Multi-emitter setup

Every emitter ships as a **separately versioned NuGet** that
`Gravity.Dsl.MsBuild` discovers at build time. Two things have to line up:

1. **The csproj lists one `<PackageReference>` per emitter the project
   wants to run.** The package's `buildTransitive/*.props` adds the
   emitter DLL to `<GravityDslEmitterAssembly>` automatically — no
   manual `<UsingTask>` or assembly path is required.
2. **`.gravity.yaml` has a top-level `emitters:` block for each one.**
   The block key matches the emitter's `TargetName` (`csharp`,
   `json-schema`, `postgres-ddl`). Each block sets at least `output:`
   (the per-emitter subdirectory under `<GravityDslOutputDir>`). If a
   block names an emitter whose package isn't referenced, the loader
   emits `CFG001 "configuration for emitter '<name>' has no registered
   target; ignoring"`.

The playground csproj (`Gravity.Playground.HrDemo.csproj`):

```xml
<ItemGroup>
  <PackageReference Include="Gravity.Dsl.MsBuild" />
  <PackageReference Include="Gravity.Dsl.Emitter.JsonSchema" />
  <PackageReference Include="Gravity.Dsl.Emitter.PostgresDdl" />
</ItemGroup>
```

The playground `.gravity.yaml`:

```yaml
emitters:
  csharp:
    output: csharp           # MUST stay 'csharp' — the auto-compile glob
    namespace: AcmeCo.Domain # is hard-coded to `Generated/csharp/**/*.cs`
    file_scoped_namespaces: true
  json-schema:
    output: json-schema
    bundle_strategy: per-entity
  postgres-ddl:
    output: postgres-ddl
    schema: acme
```

A note on `<NuGet.config>` and `<packageSourceMapping>`: this playground
restricts `Gravity.Dsl.*` to the local feed. That means **every Gravity
package the consumer references (including the transitive `Gravity.Dsl.Ast`
and `Gravity.Dsl.Emitter` brought in by the emitter packages) must be
packed into `local-packages/`**. `scripts/pack.sh` does that for you on
each run.

## Side-by-side: source ⇄ emitted C#

The `golden/` directory is the byte-identical output the emitter produces
for `Hr.gravity` — checked in for legibility and regenerated on every
build. If the emitter or the source ever drifts, the integrity check
in this README's "Verify" section catches it.

### Hr.gravity (input)

```gravity
namespace hr;

type ContactInfo {
  email:      String;
  phone:      String?;
}

enum ContractType { CDI, CDD, Freelance, Intern }

type OnboardResult     { ok: Boolean; message: String?; }
type TerminationResult { ok: Boolean; message: String?; }

entity Employee version 1 {

  identity id: UUID;

  properties {
    first_name:    String;
    last_name:     String;
    email:         String;
    hire_date:     Date;
    contract_type: ContractType;
    contact:       ContactInfo?;
  }

  lifecycle {
    states { Onboarding, Active, Terminated; }
    transitions {
      Onboarding -> Active     on Activated;
      Active     -> Terminated on Terminated;
    }
  }

  events {
    Activated  { activated_at: DateTime; };
    Terminated { terminated_at: DateTime; reason: String; };
  }

  commands {
    Onboard(first_name: String, last_name: String, email: String, hire_date: Date, contract_type: ContractType)
      returns OnboardResult
      with side_effect Activated;

    Terminate(reason: String)
      returns TerminationResult
      with side_effect Terminated;
  }
}
```

### Emitted C#

Eight files, one per top-level declaration plus per-entity bundles for
events and commands. `namespace AcmeCo.Domain.hr;` is the
`.gravity.yaml` namespace prefix joined to the `.gravity` file's own
`namespace hr;` declaration.

#### Employee.cs — the entity record

```csharp
// <auto-generated>
//     This file was generated by Gravity DSL.
//     Source: Hr.gravity
// </auto-generated>

using System;

namespace AcmeCo.Domain.hr;

public sealed record Employee(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    DateOnly HireDate,
    ContractType ContractType,
    ContactInfo? Contact,
    EmployeeState State
);
```

Notes: `identity id: UUID` becomes `Guid Id`. `hire_date: Date` becomes
`DateOnly HireDate`. The trailing `EmployeeState State` is the lifecycle
projection — every entity with a `lifecycle{}` block carries a final
state property of its own enum type.

#### EmployeeState.cs — lifecycle enum

```csharp
// <auto-generated>
//     This file was generated by Gravity DSL.
//     Source: Hr.gravity
// </auto-generated>

namespace AcmeCo.Domain.hr;

/// <summary>Lifecycle states for <see cref="Employee"/>.</summary>
public enum EmployeeState
{
    Onboarding,
    Active,
    Terminated
}
```

#### EmployeeEvents.cs — domain events bundle

```csharp
// <auto-generated>
//     This file was generated by Gravity DSL.
//     Source: Hr.gravity
// </auto-generated>

using System;

namespace AcmeCo.Domain.hr;

/// <summary>Domain event emitted by <see cref="Employee"/>.</summary>
public sealed record Activated(DateTime ActivatedAt);

/// <summary>Domain event emitted by <see cref="Employee"/>.</summary>
public sealed record Terminated(DateTime TerminatedAt, string Reason);
```

#### EmployeeCommands.cs — command bundle

```csharp
// <auto-generated>
//     This file was generated by Gravity DSL.
//     Source: Hr.gravity
// </auto-generated>

using System;

namespace AcmeCo.Domain.hr;

/// <summary>
/// Command on <see cref="Employee"/>.
/// Returns: OnboardResult
/// Side effect: Activated
/// </summary>
public sealed record Onboard(string FirstName, string LastName, string Email, DateOnly HireDate, ContractType ContractType);

/// <summary>
/// Command on <see cref="Employee"/>.
/// Returns: TerminationResult
/// Side effect: Terminated
/// </summary>
public sealed record Terminate(string Reason);
```

Notes: `returns X` and `with side_effect Y` surface as XML doc comments
on each command record. Downstream code or tooling can pick them up
from documentation without re-parsing the `.gravity` source.

#### Value types and result records

```csharp
// ContactInfo.cs
public sealed record ContactInfo(string Email, string? Phone);

// OnboardResult.cs
public sealed record OnboardResult(bool Ok, string? Message);

// TerminationResult.cs
public sealed record TerminationResult(bool Ok, string? Message);
```

#### ContractType.cs — top-level enum

```csharp
// <auto-generated>
//     This file was generated by Gravity DSL.
//     Source: Hr.gravity
// </auto-generated>

namespace AcmeCo.Domain.hr;

public enum ContractType
{
    CDI,
    CDD,
    Freelance,
    Intern
}
```

## How the MSBuild integration is wired

The two files that make this work, both shipped inside the
`Gravity.Dsl.MsBuild` NuGet:

- `buildTransitive/Gravity.Dsl.MsBuild.props` — declares the `<GravityDsl>`
  item type, default glob `**/*.gravity`, default output dir.
- `buildTransitive/Gravity.Dsl.MsBuild.targets` — declares the
  `<UsingTask>` and the `GravityDslGenerate` target. It runs
  `BeforeTargets="CoreCompile"`, calls the `GravityDslGenTask`, then
  appends `$(GravityDslOutputDir)/csharp/**/*.cs` to `@(Compile)`.

The consumer csproj only needs the `<PackageReference>`; everything else
is auto-imported by NuGet.

## Caveats this playground works around

Two repo-specific friction points the consumer csproj papers over with
documented overrides:

1. **`GravityDslOutputDir` pinned explicitly.** The shipped props
   default it to `$(IntermediateOutputPath)Generated`, but
   `$(IntermediateOutputPath)` isn't yet populated when NuGet imports
   the buildTransitive props, so the default collapses to a bare
   `Generated/` at the project root. The csproj sets
   `GravityDslOutputDir = $(BaseIntermediateOutputPath)$(Configuration)\$(TargetFramework)\Generated`
   to keep generated code inside `obj/`. See the comment in
   `Gravity.Playground.HrDemo.csproj`.

2. **`NoWarn=CS8669`.** The C# emitter writes an `<auto-generated>`
   header, which makes the compiler ignore the project-wide
   `<Nullable>enable</Nullable>` and complain about every `string?`. The
   repo's `TreatWarningsAsErrors=true` then turns the warning into a
   build error. The playground suppresses just CS8669; a clean fix
   would be for the emitter to write `#nullable enable` at the top of
   each file.

Both are good candidates for follow-up tickets against
`Gravity.Dsl.MsBuild` and `Gravity.Dsl.Emitter.CSharp` respectively.

## Verify

Re-running `dotnet build` should produce files under
`obj/Debug/net9.0/Generated/csharp/hr/` that are byte-identical to
`golden/csharp/hr/`:

```bash
dotnet build Playground.sln -c Debug
diff -r golden/csharp/hr/ Gravity.Playground.HrDemo/obj/Debug/net9.0/Generated/csharp/hr/
# (no output = identical)
```

If `diff` reports differences, either the emitter changed (refresh
`golden/`) or something in this playground drifted.
