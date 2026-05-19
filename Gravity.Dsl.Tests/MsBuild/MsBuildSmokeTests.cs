using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.MsBuild;

/// <summary>
/// AC-9.2 covered here. AC-9.3..AC-9.14, AC-9.15 deferred to Phase 9b polish.
/// End-to-end smoke: pack <c>Gravity.Dsl.MsBuild</c> to a temp feed, build a
/// consumer csproj that references it via <c>&lt;PackageReference&gt;</c>, and
/// assert (a) exit code 0 and (b) generated <c>.cs</c> files exist under
/// <c>obj/Generated/csharp/</c>. The fixture is built into a unit-test-managed
/// temp directory so the test is self-contained and CI-portable (the Phase 9
/// spike fixture at <c>/tmp/phase9-spike/</c> was the template for this layout).
/// </summary>
public sealed class MsBuildSmokeTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public void Smoke_DotnetBuild_GeneratesCSharpAndCompiles()
    {
        var msbuildDir = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.MsBuild");
        var repoRoot = new DirectoryInfo(msbuildDir).Parent!.FullName;
        var msbuildProject = Path.Combine(msbuildDir, "Gravity.Dsl.MsBuild.csproj");
        File.Exists(msbuildProject).Should().BeTrue();

        // Step 1. Allocate a counter-named, TMPDIR-rooted scratch directory via the
        // shared ScratchDir helper (FR-3045) so the smoke test's temp path is stable
        // across runs and does not race other concurrent invocations in the same workspace.
        var fixtureRoot = ScratchDir.For("smoke", repoRoot);
        var localFeed = Path.Combine(fixtureRoot, "local-packages");
        var consumerDir = Path.Combine(fixtureRoot, "consumer");
        Directory.CreateDirectory(localFeed);
        Directory.CreateDirectory(consumerDir);
        Directory.CreateDirectory(Path.Combine(consumerDir, "registry"));

        try
        {
            // Pack with an explicit version so the consumer's PackageReference resolves.
            ProcessRunner.RunDotnet(
                "pack \"" + msbuildProject + "\" --output \"" + localFeed
                    + "\" -c Debug -p:Version=0.1.0-smoke --nologo",
                workingDir: msbuildDir);

            // Step 2. Write the consumer csproj via the shared template helper so the
            // smoke and harness lanes share one source of truth.
            ConsumerCsproj.Write(
                consumerDir,
                itemFragment: string.Empty,
                nugetCacheDir: Path.Combine(fixtureRoot, ".nuget-cache"),
                packageVersion: "0.1.0-smoke",
                localFeed: localFeed);

            // Minimal Gravity source — one entity with property + lifecycle (mirrors
            // the Phase 9 spike fixture at /tmp/phase9-spike/registry/Employee.gravity
            // verbatim so any divergence between this test and the spike is visible).
            File.WriteAllText(Path.Combine(consumerDir, "registry", "Employee.gravity"),
                Fixtures.MinimalEmployeeGravity);

            // Step 3. dotnet build the consumer. The GravityDslGenerate target runs
            // before CoreCompile, writes generated .cs under obj/Generated/csharp/,
            // and the Compile item include lifts them into the C# compilation.
            var (exitCode, stdout, stderr) = ProcessRunner.RunDotnetCapture(
                "build \"" + Path.Combine(consumerDir, "Consumer.csproj") + "\" -c Debug --nologo",
                workingDir: consumerDir);

            exitCode.Should().Be(0,
                because: "dotnet build against the packed Gravity.Dsl.MsBuild must succeed.\n"
                    + "stdout:\n" + stdout + "\nstderr:\n" + stderr);

            // Step 4. Inspect the generated tree. The exact location of generated .cs is
            // configurable (GravityDslOutputDir + emitter's `output:` value combine) — for
            // AC-9.2 we just need to prove (a) codegen ran somewhere, (b) the result
            // compiled. Search the whole consumer tree for any Employee.cs under a csharp/
            // folder. This is intentionally lenient; a stricter path-shape test belongs
            // under the deferred AC-9.11 (item-metadata override) coverage.
            var generatedCs = Directory
                .GetFiles(consumerDir, "Employee.cs", SearchOption.AllDirectories)
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "csharp" + Path.DirectorySeparatorChar, System.StringComparison.Ordinal))
                .ToList();
            generatedCs.Should().NotBeEmpty(
                because: "the C# emitter must produce at least one Employee.cs under a csharp/ folder.\n"
                    + "stdout:\n" + stdout);

            // The consumer build also produces a Consumer.dll which proves the
            // generated C# actually compiles end-to-end (AC-9.2 second half).
            var binDir = Path.Combine(consumerDir, "bin", "Debug", "net9.0");
            Directory.Exists(binDir).Should().BeTrue(because: "the consumer build must produce bin/Debug/net9.0/.\nstdout:\n" + stdout);
            Directory.GetFiles(binDir, "Consumer.dll").Should().NotBeEmpty(
                because: "the generated C# files must compile as part of the consumer build");
        }
        finally
        {
            try { if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
