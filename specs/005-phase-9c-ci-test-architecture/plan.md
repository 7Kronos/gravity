# Gravity DSL — Implementation Plan (Phase 9c: CI-script test architecture for the MSBuild integration suite)

**Status:** Locked for implementation
**Date:** 2026-05-19
**Driven by:** `specs/005-phase-9c-ci-test-architecture/spec.md` and `CLAUDE.md` ("Build integration parity" architectural constraint dominant; "Deterministic output" governs FR-3023 / FR-3045; Principle III governs FR-3021 read-only-on-input; Principle IV governs FR-3022 additive CLI surface).
**Predecessor:** `specs/003-phase-9-build-integration/plan.md` (Phase 9 — MSBuild target + sample emitter). Phase 9c does not change a single FR-200..FR-260 surface element; it stands up a new test-execution shell around the unchanged MSBuild task.

---

## 1. Strategy

Three sub-phases executed **sequentially**. The split is deliberate: P9c.1 (shared library + normaliser scaffolding) is touchable by every later task, so it lands first and freezes the helper surface before any subcommand code consumes it; P9c.2 (per-AC subcommands) writes the integration logic against a stable shared API; P9c.3 (in-process test cleanup + wrapper conversion) is the irreversible step (the moment the `[Fact(Skip=...)]` lines disappear from the fast lane there is no going back, so it runs only after every harness subcommand passes locally).

A single-slice alternative was considered: "Phase 9c is small (~25-35 tasks); just sequence everything in one flat list." It was rejected because P9c.3 is risk-asymmetric — if it lands before P9c.2 closes, the fast lane goes red on `master` for the duration of the implementation, breaking the constitutional bar that "tests pass on every PR" (Quality-standards) on the very phase that exists to honour that bar. Three sub-phases turn the deletion into a tractable gate rather than a race condition.

| Sub-phase | Output | Gate (spec ACs and FRs closed) |
|---|---|---|
| P9c.1. Shared library + normaliser scaffolding | New `Gravity.Dsl.IntegrationHarness.Shared/` (in solution) with `ProcessRunner`, `WriteConsumerCsproj`, `NuGetConfigFor`, `MinimalEmployeeGravity`, `MinimalBrokenGravity`, `Sha256TreeHasher`, `ScratchDir` (FR-3045 counter). New `tools/nupkg-normaliser/Gravity.Dsl.NupkgNormaliser/` (out of solution) implementing the zip rewrite path (FR-3020..FR-3022). New `tools/nupkg-normaliser/Gravity.Dsl.NupkgNormaliser.Tests/` with the synthetic-`.nupkg` fixture (FR-3023). `Gravity.Dsl.sln` extended with the shared lib only. `Directory.Build.props` exempt list extended for the shared lib per LD-23. | FR-3002, FR-3003 (helper parity assert only), FR-3020, FR-3021, FR-3022, FR-3023, FR-3045 (counter helper). AC-9c.7, AC-9c.9, AC-9c.11. LD-20, LD-22, LD-23. |
| P9c.2. Harness console project + subcommands | New `Gravity.Dsl.IntegrationHarness/` (out of solution) with `Program.cs` (subcommand dispatch), one per-AC subcommand class (`PackDeterminismSubcommand`, `ItemMetadataOverrideSubcommand`, `HookOrderSubcommand`, `EmptyInputSubcommand`, `NoGlobalToolSubcommand`, `IncrementalBuildSubcommand`), per-step log writer, `junit.xml` writer, SDK-version drift warning. New `Gravity.Dsl.IntegrationHarness.Tests/` (out of solution) covering FR-3003 helper-parity, subcommand-dispatch unit tests, and FR-3033 stdout-shape assertions. The six subcommands actually pass `run-all` against `Gravity.Dsl.MsBuild` + `Gravity.Dsl.Emitter.Sample.Outline` packed from the working tree. | FR-3000, FR-3001, FR-3003, FR-3010, FR-3011, FR-3012, FR-3013, FR-3014, FR-3015, FR-3033, FR-3050, FR-3051. AC-9.7-pack, AC-9.11, AC-9.12, AC-9.13, AC-9.14, AC-9.15, AC-9c.2, AC-9c.4, AC-9c.5, AC-9c.8. LD-18, LD-19, LD-25. |
| P9c.3. Cleanup + wrapper conversion | `Gravity.Dsl.Tests/MsBuild/MsBuildIntegrationTests.cs` reduced to five thin `[Fact]` wrappers; `MsBuildIntegrationFixture` deleted; in-file `ProcessRunner` deleted. `Gravity.Dsl.Tests/MsBuild/DeterministicPackTests.cs` reduced to one thin `[Fact]` wrapper. `Gravity.Dsl.Tests/MsBuild/MsBuildSmokeTests.cs` edited to consume the shared `ProcessRunner`/`MinimalEmployeeGravity`. `Gravity.Dsl.Tests.csproj` gains `<ProjectReference>` to the shared lib. Slow lane (`dotnet test --filter "Category=Slow"`) passes end-to-end against a clean checkout. | FR-3004, FR-3030, FR-3031, FR-3032, FR-3040 (carry note only — no workflow file). AC-9c.1, AC-9c.3. LD-21. |

Phase 9c is **strictly additive at the build / packaging layer**: zero changes to compiler, AST, resolver, validator, emitter host, CLI, or MSBuild task source. The only deletions are skipped-fact bodies (FR-3030 / FR-3031); every other touch is "new file" or "shared-helper reference replaces in-file duplicate".

## 2. Project layout

Mirrors the predecessor plan; restricted to additions and touches required by Phase 9c. The two new in-repo NuGet-shaped trees (`Gravity.Dsl.IntegrationHarness.*`, `tools/nupkg-normaliser/*`) are deliberately disjoint from the production source tree's solution membership: only `Gravity.Dsl.IntegrationHarness.Shared/` is added to `Gravity.Dsl.sln` (because both fast-lane tests and the harness consume it); the harness console, its test sibling, the normaliser, and the normaliser-tests project are invoked via `dotnet run --project <path>` / `dotnet test <path>/<csproj>` directly and never appear in solution sweeps (LD-19).

```
Gravity.Dsl/
├── Gravity.Dsl.IntegrationHarness.Shared/         # NEW: shared helpers (IN solution)
│   ├── ProcessRunner.cs                           # NEW: concurrent stdout/stderr drain + 5-min timeout
│   ├── WriteConsumerCsproj.cs                     # NEW: csproj template factory (single source of truth)
│   ├── NuGetConfigFactory.cs                      # NEW: NuGetConfigFor(localFeed) helper
│   ├── Fixtures.cs                                # NEW: MinimalEmployeeGravity + MinimalBrokenGravity consts
│   ├── Sha256TreeHasher.cs                        # NEW: (relative-path, content-bytes) tuple hasher
│   ├── ScratchDir.cs                              # NEW: TMPDIR-rooted, counter-named, FileShare.None lock
│   └── Gravity.Dsl.IntegrationHarness.Shared.csproj  # NEW: net9.0, TreatWarningsAsErrors=true
├── Gravity.Dsl.IntegrationHarness/                # NEW: harness console project (NOT in solution)
│   ├── Program.cs                                 # NEW: subcommand dispatcher + SDK-version drift warning
│   ├── HarnessOptions.cs                          # NEW: --config / --out / --filter parsing
│   ├── HarnessRunner.cs                           # NEW: run-all orchestration, junit.xml writer
│   ├── HarnessLog.cs                              # NEW: per-step log file + AC-<id> PASS stdout emitter
│   ├── Subcommands/
│   │   ├── ISubcommand.cs                         # NEW: contract: Name, AcId, Run(ScratchDir, HarnessLog) -> Result
│   │   ├── PackDeterminismSubcommand.cs           # NEW: AC-9.7-pack (FR-3015)
│   │   ├── ItemMetadataOverrideSubcommand.cs      # NEW: AC-9.11 (FR-3010)
│   │   ├── HookOrderSubcommand.cs                 # NEW: AC-9.12 (FR-3011)
│   │   ├── EmptyInputSubcommand.cs                # NEW: AC-9.13 (FR-3012)
│   │   ├── NoGlobalToolSubcommand.cs              # NEW: AC-9.14 (FR-3013)
│   │   └── IncrementalBuildSubcommand.cs          # NEW: AC-9.15 (FR-3014)
│   ├── HarnessRuleIds.cs                          # NEW: HARN001..HARN010 constants (FR-3050)
│   └── Gravity.Dsl.IntegrationHarness.csproj      # NEW: net9.0, NOT in solution
├── Gravity.Dsl.IntegrationHarness.Tests/          # NEW: harness self-tests (NOT in solution)
│   ├── HelperParityTests.cs                       # FR-3003: csproj hash equality across consumers
│   ├── SubcommandDispatchTests.cs                 # FR-3000: argv → subcommand routing
│   ├── HarnessLogTests.cs                         # FR-3033: stdout shape; AC-9c.8 determinism
│   └── Gravity.Dsl.IntegrationHarness.Tests.csproj
├── tools/nupkg-normaliser/                        # NEW: post-pack normaliser (NOT in solution)
│   ├── Gravity.Dsl.NupkgNormaliser/
│   │   ├── Program.cs                             # CLI entry: --input, --output, --version
│   │   ├── NupkgNormalizer.cs                     # static Normalize(input, output) + helpers
│   │   ├── PsmdcpRenamer.cs                       # SHA-256 of decompressed bytes → filename
│   │   ├── RelsRewriter.cs                        # XPath/regex over _rels/.rels; only .psmdcp-targeting <Relationship>
│   │   └── Gravity.Dsl.NupkgNormaliser.csproj
│   └── Gravity.Dsl.NupkgNormaliser.Tests/
│       ├── DeterminismTests.cs                    # FR-3023 byte-equality
│       ├── IdempotenceTests.cs                    # AC-9c.11 fixed-point
│       ├── BoundaryTests.cs                       # manifest-pointer <Relationship> is NOT rewritten
│       └── Gravity.Dsl.NupkgNormaliser.Tests.csproj
├── tests/fixtures/nupkg-normaliser/               # NEW: synthetic fixture for normaliser tests
│   ├── pack-a.nupkg                               # baseline
│   └── pack-b.nupkg                               # same content, different .psmdcp GUID + Target Id
├── Gravity.Dsl.Tests/MsBuild/                     # TOUCH: see §3.5
│   ├── MsBuildIntegrationTests.cs                 # REDUCED to 5 wrapper Facts (FR-3030)
│   ├── DeterministicPackTests.cs                  # REDUCED to 1 wrapper Fact (FR-3031)
│   └── MsBuildSmokeTests.cs                       # EDITED: shared helpers via ProjectReference
├── Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj     # TOUCH: <ProjectReference> to shared lib
├── Directory.Build.props                          # TOUCH: extend BannedSymbols carve-out for shared lib
├── artifacts/integration-harness/                 # NEW (gitignored): runtime artefact root
│   ├── junit.xml
│   └── <subcommand>-run<N>/                       # per-subcommand scratch + log directory
└── Gravity.Dsl.sln                                # TOUCH: add Gravity.Dsl.IntegrationHarness.Shared ONLY
```

The four new csproj trees that stay **out** of `Gravity.Dsl.sln` carry the structural enforcement of LD-19's "different execution shell, same code" rule. A future developer attempting `<ProjectReference Include="..\Gravity.Dsl.IntegrationHarness\Gravity.Dsl.IntegrationHarness.csproj" />` from a fast-lane test project must explicitly point at a project path the solution does not know about, which surfaces in code review as a deliberate cross-shell bridge rather than an accident.

## 3. Module-level architecture

### 3.1 Shared library (`Gravity.Dsl.IntegrationHarness.Shared`)

The shared library extracts every helper currently duplicated between `MsBuildIntegrationTests.cs` (which is being reduced) and `MsBuildSmokeTests.cs` (which keeps its `[Fact]` and its slow-lane membership but loses the in-file duplicates). The extracted types are `public` so both consumers can call them; the project targets `net9.0` and inherits the test-project `BannedSymbols` exemption (per LD-23 — the helpers legitimately need `ScratchDir` to lay out per-run directories under `TMPDIR`).

`ProcessRunner` mirrors the existing `MsBuildSmokeTests.RunDotnetCapture` (lines 154-179) verbatim except for the namespace change: concurrent stdout/stderr drain via `Task.WhenAll`, 5-minute (`300_000` ms) timeout, `Process.Kill(entireProcessTree: true)` on timeout, `UseShellExecute = false`, `CreateNoWindow = true`. The 4 KB pipe-buffer deadlock comment is preserved as XML doc.

`WriteConsumerCsproj` factors the existing duplicate `WriteConsumerCsproj` helper (currently `MsBuildIntegrationTests.cs` lines 111-133) into a public method:

```csharp
public static string WriteConsumerCsproj(
    string consumerDir,
    string itemFragment,
    string nugetCacheDir,
    string packageVersion,
    string targetFramework = "net9.0")
{
    var csprojPath = Path.Combine(consumerDir, "Consumer.csproj");
    File.WriteAllText(csprojPath, /* template using packageVersion + itemFragment */);
    File.WriteAllText(Path.Combine(consumerDir, "nuget.config"),
        NuGetConfigFactory.NuGetConfigFor(/* localFeed */));
    return csprojPath;
}
```

The template is byte-stable (no `Environment.NewLine`; LF only) and uses ordinal `string.Replace` for backslash escaping on the `RestorePackagesPath` row. FR-3003 / AC-9c.3 mandate that the harness and the fast lane both reach this function — a unit test in `Gravity.Dsl.IntegrationHarness.Tests/HelperParityTests.cs` invokes it from both sides and SHA-256s the resulting csproj file to assert equality.

`Sha256TreeHasher` walks `(relativePath, fileContents)` tuples in `StringComparer.Ordinal` order, hashes each `relativePath + "\n" + sha256(content)` through `SHA256.HashData`, and returns the final hex digest. Used by AC-9.5 (CLI/MSBuild parity, existing) and forward-compatible for any future "two trees must agree byte-for-byte" assertion.

`ScratchDir.For(subcommandName)` is the FR-3045 implementation:

```csharp
public static string For(string subcommandName, string workspaceRoot)
{
    var counterPath = Path.Combine(workspaceRoot, "artifacts", "integration-harness", ".counter");
    Directory.CreateDirectory(Path.GetDirectoryName(counterPath)!);
    int next;
    using (var fs = new FileStream(counterPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
    using (var reader = new StreamReader(fs))
    using (var writer = new StreamWriter(fs))
    {
        var current = int.TryParse(reader.ReadToEnd(), out var n) ? n : 0;
        next = current + 1;
        fs.SetLength(0);
        writer.Write(next.ToString(CultureInfo.InvariantCulture));
    }
    var tmp = Environment.GetEnvironmentVariable("TMPDIR")
              ?? Environment.GetEnvironmentVariable("TEMP")
              ?? "/tmp";
    var dir = Path.Combine(tmp, $"gravity-{subcommandName}-run{next}");
    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    Directory.CreateDirectory(dir);
    return dir;
}
```

The `FileShare.None` lock on the counter file is the load-bearing detail per the spec.md §6 risk register row "`<run-id>` collision under parallel invocation": concurrent harness invocations from the same workspace queue rather than collide. The reset-to-1 semantics (counter file deleted by `artifacts/` cleanup; AC-9c.8) guarantee that two consecutive invocations from a clean workspace both produce `run1` scratch dirs and identical `junit.xml`.

### 3.2 Normaliser (`Gravity.Dsl.NupkgNormaliser`)

The normaliser is a console project at `tools/nupkg-normaliser/Gravity.Dsl.NupkgNormaliser/`. CLI surface is `--input <path> --output <path>` (FR-3020) and `--version` (FR-3022). `Program.cs` is a thin argv parser; the work happens in `NupkgNormalizer.Normalize(string inputPath, string outputPath)`. Pseudocode for the rewrite path:

```csharp
public static void Normalize(string inputPath, string outputPath)
{
    // FR-3021: read-only on input; atomic-write on output.
    var tempOutput = outputPath + ".tmp";
    using (var inputStream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    using (var inputZip = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: false))
    {
        // Phase 1: scan input. Find the .psmdcp entry, compute its content hash, decide its new name.
        ZipArchiveEntry? psmdcpEntry = inputZip.Entries
            .FirstOrDefault(e => e.FullName.StartsWith(
                "package/services/metadata/core-properties/", StringComparison.Ordinal)
                && e.FullName.EndsWith(".psmdcp", StringComparison.Ordinal));
        if (psmdcpEntry is null)
            throw new InvalidOperationException("input .nupkg has no .psmdcp entry");

        byte[] psmdcpBytes;
        using (var s = psmdcpEntry.Open())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            psmdcpBytes = ms.ToArray();
        }
        // FR-3020 step (d): rename to sha256-of-decompressed-bytes.psmdcp, full 64 hex chars.
        var psmdcpHash = Convert.ToHexString(SHA256.HashData(psmdcpBytes)).ToLowerInvariant();
        var newPsmdcpPath = $"package/services/metadata/core-properties/{psmdcpHash}.psmdcp";

        // Phase 2: load _rels/.rels and rewrite ONLY the .psmdcp-targeting <Relationship>'s Target.
        // The manifest-pointer <Relationship Id="RAC971DF315D82D83" Target="/<id>.nuspec" /> is
        // content-deterministic per the spike (/tmp/phase9c-spike/) and MUST NOT be touched.
        ZipArchiveEntry relsEntry = inputZip.Entries
            .Single(e => e.FullName == "_rels/.rels");
        string relsXml;
        using (var s = relsEntry.Open()) using (var r = new StreamReader(s))
            relsXml = r.ReadToEnd();

        var newRelsXml = RelsRewriter.RewritePsmdcpTarget(relsXml, newTarget: "/" + newPsmdcpPath);

        // Phase 3: build the normalised entry set, sorted by NEW path under StringComparer.Ordinal.
        var normalised = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var e in inputZip.Entries)
        {
            if (e == psmdcpEntry)
            {
                normalised[newPsmdcpPath] = psmdcpBytes;
                continue;
            }
            if (e == relsEntry)
            {
                normalised["_rels/.rels"] = Encoding.UTF8.GetBytes(newRelsXml);
                continue;
            }
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            normalised[e.FullName] = ms.ToArray();
        }

        // Phase 4: emit. Sorted iteration + zero timestamps + no extra-fields (FR-3020 a/b/c).
        using var outFile = new FileStream(tempOutput, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var outZip = new ZipArchive(outFile, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var (path, bytes) in normalised)
            {
                var entry = outZip.CreateEntry(path, CompressionLevel.Optimal);
                entry.LastWriteTime = DateTimeOffset.FromUnixTimeSeconds(0);  // FR-3020 (b)
                using var es = entry.Open();
                es.Write(bytes, 0, bytes.Length);
            }
        }
    }
    File.Move(tempOutput, outputPath, overwrite: true);
}
```

`RelsRewriter.RewritePsmdcpTarget` is the second load-bearing piece. The spike (`/tmp/phase9c-spike/ex1/_rels/.rels` and `ex2/_rels/.rels`) confirmed the two `<Relationship>` entries differ only in the `.psmdcp`-targeting row: same `Type`, same XMLNS, divergent `Target` and `Id`. The rewriter:

1. Parses the XML with `XDocument.Parse(input, LoadOptions.PreserveWhitespace)`.
2. Selects the `<Relationship>` element whose `Type` attribute equals `http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties` (this is the `.psmdcp` row — it is the **only** relationship in the file with this exact `Type` value; the manifest pointer has `Type="http://schemas.microsoft.com/packaging/2010/07/manifest"`).
3. Updates the selected element's `Target` attribute to the new `/package/services/metadata/core-properties/<hash>.psmdcp` value. **The `Id` attribute is left alone**, even though it currently carries a per-pack random value, because the spike showed `Id` rewrites add zero determinism value (the surrounding bytes of the `.rels` document already canonicalise under the SHA-256 over `relsXml` when `Target` is stable, and the manifest-pointer relationship's `Id` `RAC971DF315D82D83` is byte-stable across packs and is the canonical reference).
4. Serialises back via `XDocument.Save` with `SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces` so whitespace does not leak into the byte comparison.

The XPath equivalent (for the test pseudocode in `BoundaryTests.cs`) is:

```
/Relationships/Relationship[@Type='http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties']/@Target
```

with the assertion that exactly one element matches and the matching element's `@Target` ends with `.psmdcp`. A `BoundaryTests` row asserts the **other** relationship (`Type="http://schemas.microsoft.com/packaging/2010/07/manifest"`) is unchanged byte-for-byte across normalise input and output.

Idempotence (FR-3023, AC-9c.11) falls out of the design: a second pass through `Normalize` finds the same `.psmdcp` content (the rename is content-derived, so the second pass renames `<hash>.psmdcp` to `<hash>.psmdcp` — no-op), produces an identical `_rels/.rels` rewrite (the `Target` already equals the new value), and emits an identical sorted entry set with identical zero timestamps. The `DeterminismTests` (run twice, byte-equal) and `IdempotenceTests` (run thrice, runs 2 and 3 byte-equal) test rows exist as defence-in-depth against a future contributor introducing a non-idempotent transformation.

### 3.3 Harness console project (`Gravity.Dsl.IntegrationHarness`)

`Program.cs` is the entry point. Argv layout: `<subcommand> [--config <Debug|Release>] [--out <dir>] [--filter <pattern>]`. Legal subcommand tokens: `run-ac-9.7-pack`, `run-ac-9.11`, `run-ac-9.12`, `run-ac-9.13`, `run-ac-9.14`, `run-ac-9.15`, `run-all`. Dispatch is a switch over the leading token; unknown tokens emit `HARN010`-reserved-or-typo to stderr and exit non-zero.

```csharp
public static int Main(string[] args)
{
    var opts = HarnessOptions.Parse(args);  // raises on malformed argv
    // FR-3001: warn (not fail) on patch-version drift from global.json's 9.0.314 pin.
    SdkVersionCheck.WarnIfDrift(pinned: "9.0.314");
    var runner = new HarnessRunner(opts);
    var subcommands = new List<ISubcommand>
    {
        new PackDeterminismSubcommand(),
        new ItemMetadataOverrideSubcommand(),
        new HookOrderSubcommand(),
        new EmptyInputSubcommand(),
        new NoGlobalToolSubcommand(),
        new IncrementalBuildSubcommand(),
    };
    return opts.Subcommand switch
    {
        "run-all" => runner.RunAll(subcommands),
        var name => runner.RunOne(subcommands.Single(s => s.SubcommandName == name)),
    };
}
```

Each `ISubcommand` exposes `SubcommandName`, `AcId`, and `Run(ScratchDir scratch, HarnessLog log) -> SubcommandResult`. The runner orchestrates: it allocates a `ScratchDir` per subcommand (LD-25 — no amortised pack across subcommands), invokes the subcommand, captures the result, writes a per-step log file under `--out`, accumulates the JUnit XML rows, and emits `AC-<id> PASS` (success) or the failure triple `HARN<NNN> / AC-<id> / <log path>` per FR-3033.

The `run-all` final-line summary `Phase 9c integration harness: N/N steps passed.` (AC-9c.4) is the only stdout line `HarnessRunner` emits after every per-subcommand line.

### 3.4 Per-subcommand pack strategy (LD-25)

Every subcommand that needs the MSBuild package (all of them except `run-ac-9.14`'s `dotnet tool list -g` step) follows the same scratch lifecycle:

1. `var scratch = ScratchDir.For(subcommandName, workspaceRoot);` — deterministic path under `TMPDIR`.
2. `var localFeed = Path.Combine(scratch, "local-packages");` — per-subcommand feed.
3. `ProcessRunner.Run("dotnet pack Gravity.Dsl.MsBuild.csproj -c Release -o " + localFeed, repoRoot);` — fresh pack, owned by this subcommand.
4. For AC-9.7-pack (`PackDeterminismSubcommand`), pack a **second time** into `scratch/local-packages-2/` and normalise both, then SHA-256 the normalised bytes. The two-pack-in-one-scratch shape is local to this subcommand only; the other five subcommands pack once.
5. Compose the consumer csproj under `Path.Combine(scratch, "consumer")` via `WriteConsumerCsproj` (shared lib).
6. `ProcessRunner.Run("dotnet build " + consumerCsproj, scratch);` — exit code + stdout captured.
7. Assert per the subcommand's FR-301x contract; on failure surface the corresponding `HARN<NNN>`.

The ~5 second × 6 subcommand cost (LD-25) is the design choice the §6 risk register row "`dotnet pack` timing differences across Linux and macOS runners" guards. No subcommand reaches into another subcommand's scratch dir.

### 3.5 xUnit wrapper Facts (FR-3004)

The wrapper Facts in `Gravity.Dsl.Tests/MsBuild/MsBuildIntegrationTests.cs` are the ~3 LOC pattern LD-21 mandates. Concrete shape:

```csharp
[Trait("Category", "Slow")]
public sealed class MsBuildIntegrationTests
{
    [Fact]
    public void AC_9_11_ItemMetadataOverride()
    {
        var (exit, stdout, _) = ProcessRunner.RunDotnetCapture(
            "run --project Gravity.Dsl.IntegrationHarness -- run-ac-9.11",
            workingDir: RepoRoot());
        exit.Should().Be(0);
        stdout.Should().Contain("AC-9.11 PASS");
    }

    [Fact] public void AC_9_12_HookOrder() => RunSubcommand("run-ac-9.12", "AC-9.12 PASS");
    [Fact] public void AC_9_13_EmptyInput() => RunSubcommand("run-ac-9.13", "AC-9.13 PASS");
    [Fact] public void AC_9_14_NoGlobalTool() => RunSubcommand("run-ac-9.14", "AC-9.14 PASS");
    [Fact] public void AC_9_15_IncrementalBuild() => RunSubcommand("run-ac-9.15", "AC-9.15 PASS");

    private static void RunSubcommand(string subcommand, string expectedPassMarker) { /* 3 LOC */ }
}
```

The first Fact is spelled out in full for the `using` directives' worth of context; the remaining four are expression-bodied dispatch to a `RunSubcommand` helper. `DeterministicPackTests.cs` carries the single AC-9.7-pack wrapper following the same shape. The five `MsBuildIntegrationTests` Facts + the one `DeterministicPackTests` Fact = six wrappers, one per FR-3010..FR-3015 subcommand, per AC-9c.1.

Per-AC discoverability via `dotnet test --filter "FullyQualifiedName~AC_9_11"` is preserved by the explicit Fact-name encoding (the underscored `AC_9_11_ItemMetadataOverride` reads cleanly under `--filter` and remains greppable).

### 3.6 Solution / csproj changes

Only one project is added to `Gravity.Dsl.sln`: `Gravity.Dsl.IntegrationHarness.Shared` (LD-19 / LD-20). The harness console, the harness self-tests, the normaliser, and the normaliser self-tests stay out. `Gravity.Dsl.Tests/Gravity.Dsl.Tests.csproj` gains exactly one `<ProjectReference Include="..\Gravity.Dsl.IntegrationHarness.Shared\Gravity.Dsl.IntegrationHarness.Shared.csproj" />`. No other csproj is touched.

`Directory.Build.props` is edited per LD-23: the existing `<Choose>/<When>` exempt list is extended with `'$(MSBuildProjectName)' != 'Gravity.Dsl.IntegrationHarness.Shared'` (exact-equality, no prefix/substring matching — same convention as the existing Phase 9 row). The exempt-list comment block grows by one paragraph documenting the Phase 9c addition; AC-9c.9 pins the resulting exempt set at four (`Cli`, `Tests`, `MsBuild`, `IntegrationHarness.Shared`).

## 4. Test strategy

Three test tiers mirror the predecessor plan; the harness's self-test tier is new.

### 4.1 Normaliser unit tests (`Gravity.Dsl.NupkgNormaliser.Tests`)

Out-of-solution xUnit project invoked via `dotnet test tools/nupkg-normaliser/Gravity.Dsl.NupkgNormaliser.Tests/Gravity.Dsl.NupkgNormaliser.Tests.csproj`. Rows:

- **`DeterminismTests.Normalize_TwiceAgainstSameInput_ByteEqual`** — runs `NupkgNormalizer.Normalize(fixturePath, out1)` and `Normalize(fixturePath, out2)`, asserts `SHA256(out1) == SHA256(out2)`. Pins FR-3023.
- **`IdempotenceTests.Normalize_Thrice_Runs2And3ByteEqual`** — runs three passes; asserts pass-2 and pass-3 are byte-equal. Pins AC-9c.11 / FR-3023.
- **`BoundaryTests.ManifestPointerRelationship_NotRewritten`** — extracts `_rels/.rels` from input and output, asserts the `<Relationship Type="...manifest">` row's bytes (the manifest pointer with `Id="RAC971DF315D82D83"`) is identical input-to-output. Pins LD-22 / FR-3020 step (e) boundary.
- **`BoundaryTests.PsmdcpPointerRelationship_TargetRewritten`** — asserts the `<Relationship Type="...core-properties">` row's `Target` attribute changes input-to-output and the new value ends with `.psmdcp`. Defence-in-depth against the rewriter being a no-op.

The synthetic fixture pair (`tests/fixtures/nupkg-normaliser/pack-a.nupkg`, `pack-b.nupkg`) is a tiny `.nupkg` (a single `lib/net9.0/Hello.dll` plus the standard NuGet metadata) hand-built once and committed. Generating it from a live `dotnet pack` was rejected because the test would then depend on the existing `Gravity.Dsl.MsBuild` build product, which is itself the test subject — circular and brittle.

### 4.2 Harness self-tests (`Gravity.Dsl.IntegrationHarness.Tests`)

Out-of-solution xUnit project. Rows:

- **`HelperParityTests.WriteConsumerCsproj_FastLaneAndHarness_ByteEqual`** — pins FR-3003 / AC-9c.3 by invoking the shared helper from both sides and SHA-256-comparing.
- **`SubcommandDispatchTests.UnknownToken_ExitsNonZero`** — pins FR-3000 argv routing.
- **`HarnessLogTests.RunAll_Success_FinalLineMatches`** — pins FR-3033 stdout-shape (`Phase 9c integration harness: N/N steps passed.`).
- **`HarnessLogTests.RunAll_TwiceAgainstCleanWorkspace_JunitXmlByteEqual`** — pins AC-9c.8 (sequence-number counter resets cleanly).

The self-tests do NOT actually pack/build (that is the harness's job; it would be circular to do it here). Instead, they exercise the harness's own argv parsing, log writers, and JUnit XML emitter against stubbed `ISubcommand` implementations that return synthetic `SubcommandResult.Pass()`/`Fail(...)` values.

### 4.3 Integration shape (harness `run-all`)

The harness itself is the integration-test tier. `dotnet run --project Gravity.Dsl.IntegrationHarness -- run-all --config Release` against a clean workspace exits 0 and prints `Phase 9c integration harness: 6/6 steps passed.` (AC-9c.4). Each subcommand under the hood asserts its FR-301x contract; collectively they pin AC-9.7-pack, AC-9.11..AC-9.15 (group i in spec §5.1).

The xUnit wrappers in `Gravity.Dsl.Tests/MsBuild/{MsBuildIntegrationTests,DeterministicPackTests}.cs` give `dotnet test --filter "Category=Slow"` access to the same coverage; pre-merge, a developer runs both `dotnet test --filter "Category=Slow"` and `dotnet run --project Gravity.Dsl.IntegrationHarness -- run-all` and both should be green.

## 5. Determinism contract

Two banned-API analyzer scopes apply to Phase 9c projects:

| Project | Inside analyzer scope? | Notes |
|---|---|---|
| `Gravity.Dsl.IntegrationHarness` | **Yes** (default). | LD-23. The harness driver compares hashes and prints AC verdicts; no legitimate clock, GUID, or machine-name read. `BannedSymbols.txt` enforces. |
| `Gravity.Dsl.IntegrationHarness.Shared` | **No** (exempt). | New row in `Directory.Build.props` exempt list. The helpers need `ScratchDir` access (which itself uses `Environment.GetEnvironmentVariable("TMPDIR")` — NOT banned `Path.GetTempPath()`). |
| `Gravity.Dsl.IntegrationHarness.Tests` | **No** (inherits via test convention). | Tests need flex per the Phase 0–3 precedent. |
| `Gravity.Dsl.NupkgNormaliser` | **Yes** (default). | Pure zip/XML transformation, no clock / GUID / machine-name surface. |
| `Gravity.Dsl.NupkgNormaliser.Tests` | **No** (inherits via test convention). | Same precedent. |

`Environment.GetEnvironmentVariable("TMPDIR")` is the documented escape hatch for cross-platform temp-dir resolution (FR-3045). The concrete pattern is one helper, `ScratchDir.GetTempRoot()`:

```csharp
private static string GetTempRoot()
    => Environment.GetEnvironmentVariable("TMPDIR")
       ?? Environment.GetEnvironmentVariable("TEMP")
       ?? "/tmp";
```

This is **not** `Path.GetTempPath()` (which is on `BannedSymbols.txt` line 9). On Linux/macOS, `TMPDIR` is the conventional environment variable; on Windows (not in the CI matrix per NG-3, but possible developer scenario), `TEMP` provides equivalence; `/tmp` is the documented fallback. The helper lives in the shared library, not in `Gravity.Dsl.IntegrationHarness`, because the harness project is inside the analyzer scope and a centralised escape hatch is cleaner than per-call-site suppression comments.

## 6. Risk register (Phase 9c implementation surface)

Pulled forward from spec.md §6 with implementation-level rows added.

| Risk | Surface | Mitigation |
|---|---|---|
| Harness drift from fast lane. | Two execution paths consume the same fixtures via different entry points. | LD-20 / FR-3002 / AC-9c.3 enforce single-locus helpers. `HelperParityTests` (§4.2) asserts byte equality at build time. The xUnit wrapper Facts (FR-3004) make the slow-lane subcommand the literal body of every previously-skipped Fact. |
| Post-pack normaliser fragility. | SDK 10+ may add a new non-deterministic surface in `_rels/.rels`, `[Content_Types].xml`, or the `.nuspec`. | FR-3015 logs pre-normalisation hashes alongside post; `IdempotenceTests` (§4.1) guards against a normaliser pass introducing new non-determinism; `BoundaryTests.ManifestPointerRelationship_NotRewritten` pins the manifest pointer as the canonical reference. The deferred direct-PackCommand alternative (LD-22) is the documented escape hatch. |
| Over-aggressive `_rels/.rels` rewriting. | The manifest pointer `<Relationship Id="RAC971DF315D82D83">` is content-deterministic; rewriting it introduces divergence the SDK does not produce. | `RelsRewriter.RewritePsmdcpTarget` selects by `Type` attribute (`...metadata/core-properties` only); the manifest-pointer relationship's `Type` (`...packaging/2010/07/manifest`) is disjoint and is structurally invisible to the rewriter. `BoundaryTests` pins this. |
| `dotnet pack` timing on macOS leg. | Per-subcommand pack costs ~5s × 6 = ~30s; first-cold-restore can spike. | LD-25 accepts the 30-second baseline. NuGet package cache (`~/.nuget/packages/`) is preserved across CI runs (when the workflow exists per FR-3040 follow-on); locally, the harness invokes `dotnet pack` directly against the in-tree csproj and does not re-restore Gravity packages. |
| `<run-id>` counter race. | Concurrent harness invocations from the same workspace would collide on the counter file. | `FileShare.None` on the counter file (`ScratchDir.For` §3.1) queues concurrent reads. The harness is a developer / CI step, not a daemon — sequential is the expected mode. |
| Skipped-fact deletion loses test intent. | The English-language assertion comments in the original `MsBuildIntegrationTests.cs` go away with the bodies. | FR-3010..FR-3015 carry the prose verbatim into spec.md; the C# harness subcommand classes carry the same prose as XML doc-comments. The wrapper Fact names (`AC_9_11_ItemMetadataOverride`) carry the AC id into `dotnet test --filter`. |
| Implementation-level: shared-lib project ordering. | If `Gravity.Dsl.IntegrationHarness.Shared` lands in the solution before `Gravity.Dsl.Tests` is edited to reference it, the solution builds but the in-file duplicates in `MsBuildSmokeTests.cs` and `MsBuildIntegrationTests.cs` go un-deleted until P9c.3. The interim state is functional but redundant — two `RunDotnetCapture` implementations co-existing. | Treat as expected interim state; P9c.3 closes the redundancy. A CI lint that asserts "no `static.*RunDotnetCapture` outside the shared lib" (AC-9c.3) catches a P9c.3 regression but does not need to hold during P9c.1/P9c.2. |
| Implementation-level: harness invocation cost in the fast lane. | The wrapper Facts (FR-3004) each invoke `dotnet run --project Gravity.Dsl.IntegrationHarness` which itself recompiles the harness on every run. Six such invocations on a cold cache could push the slow-lane test time materially up. | The wrappers carry `[Trait("Category", "Slow")]` and run only in the slow lane (where the budget tolerates this). For local iteration, developers run the harness directly via `dotnet run` once and skip the wrappers. The harness can be `dotnet build`-ed once and then `dotnet <path-to-dll>` invoked, bypassing the recompile; this micro-optimisation is deferred unless real wall-time budget pressure shows up. |

## 7. Acceptance test order

The order the implementation should make each AC pass:

1. **AC-9c.9** (`BannedSymbols` exempt list extended). Lands in P9c.1's first commit; protects every subsequent project from accidental clock reads.
2. **AC-9c.7** (normaliser determinism). Pinned by `DeterminismTests`; gates P9c.1 closure.
3. **AC-9c.11** (normaliser idempotence). Pinned by `IdempotenceTests`; lands alongside AC-9c.7.
4. **AC-9c.3** (shared helpers are shared). Pinned by `HelperParityTests` and a grep-step in CI; lands at the end of P9c.1.
5. **AC-9c.2** (harness builds cleanly outside the solution). Lands in P9c.2's first commit (`dotnet build` of the harness csproj).
6. **AC-9.7-pack** (PackDeterminism subcommand). First P9c.2 subcommand to land — exercises the normaliser end-to-end.
7. **AC-9.11, AC-9.12, AC-9.13** (ItemMetadataOverride, HookOrder, EmptyInput subcommands). The "simple" three — single-build assertions.
8. **AC-9.14** (NoGlobalTool subcommand). Adds the `dotnet tool list -g` pre-flight.
9. **AC-9.15** (IncrementalBuild subcommand). Last to land in P9c.2 — three-build flow with `touch` in the middle.
10. **AC-9c.4** (harness run-all green on a clean checkout). Falls out of P9c.2 close — `run-all` invokes the six subcommands now landed.
11. **AC-9c.5** (planted regression). Pinned by a dedicated fixture under `Gravity.Dsl.IntegrationHarness.Tests/PlantedRegressionTests.cs`; lands in P9c.2.
12. **AC-9c.8** (harness determinism, fixed `<run-id>`). Pinned by `HarnessLogTests.RunAll_Twice...`; lands in P9c.2.
13. **AC-9c.1** (no skipped Facts). The P9c.3 gate. The grep pass is run in CI as a hard fail.
14. **AC-9c.10** (constitutional principle traceback). Manual review pass at the end of P9c.3 before lock.

## 8. Out of scope confirmations

Restated in implementation terms; no row introduces new work.

- **NG-1.** Fast lane is unchanged except for the six wrapper Facts and the in-file duplicate deletions. No new `Gravity.Dsl.Tests/` files; no removal of any existing `[Fact]` outside the skipped-fact set.
- **NG-2.** No CI step pins SDK versions beyond `global.json`'s `9.0.314`; the harness's startup warning (FR-3001) is informational only.
- **NG-3.** No `runs-on: windows-latest` row in any future ci.yml; FR-3040 carries forward unchanged.
- **NG-4.** Zero source-code touches under `Gravity.Dsl.MsBuild/`, `Gravity.Dsl.Compiler/`, `Gravity.Dsl.Ast/`, `Gravity.Dsl.Emitter/`, `Gravity.Dsl.Emitter.CSharp/`, `Gravity.Dsl.Emitter.JsonSchema/`, `Gravity.Dsl.Cli/`. Verified via `git diff --stat` filter at PR time.
- **NG-5.** No reference to `NuGet.Packaging.PackCommand`, `NuGet.Build.Tasks.Pack`, or any NuGet API beyond `System.IO.Compression.ZipArchive`. The normaliser is pure BCL.
- **NG-6.** No `AnsiColor.cs`, no `--watch`, no `--verbose`, no progress reporting. The harness emits exactly the stdout shape FR-3033 specifies.
- **NG-7.** Zero HTTP client code in the harness, in the shared lib, in the normaliser. No `System.Net.Http` references.
- **NG-8.** `tests/integration/msbuild-smoke*/` stays as-is. The harness reads the fixtures in place; no relocation, no consolidation.
- **NG-9.** No `.github/workflows/ci.yml` file created. FR-3040 carries the SHOULD-only follow-on note; no Phase 9c task delivers the file.
- **NG-10.** Phase 9 NG-1..NG-10 + Phase 4 NG-1..NG-11 + Phase 0–3 / Phase 8 NG carry-over all stand. No new functional surface in the DSL grammar, AST, resolver, validator, emitter host, or MSBuild task.

## 9. Revision history

- 2026-05-19 — Initial lock. Phase 9c implementation plan authored against `spec.md` (Phase 9c CI-script test architecture). Three sub-phases sequenced (P9c.1 shared lib + normaliser, P9c.2 harness console + subcommands, P9c.3 cleanup + wrapper conversion); project layout, module-level architecture, normaliser pseudocode with concrete XPath for the `.psmdcp`-targeting Relationship, harness subcommand-dispatch shape, xUnit-wrapper Fact pattern, determinism contract (TMPDIR-based `ScratchDir`), test strategy (normaliser unit, harness self, integration `run-all`), risk register, acceptance test order, and out-of-scope acknowledgements documented. Per-subcommand pack strategy locked at LD-25 (~30s budget accepted). Manifest-pointer `<Relationship Id="RAC971DF315D82D83">` boundary pinned by `BoundaryTests` against the spike artefacts.
