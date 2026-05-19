using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Gravity.Dsl.IntegrationHarness.Subcommands;
using Xunit;

namespace Gravity.Dsl.IntegrationHarness.Tests;

/// <summary>
/// AC-9c.5: planted regression — the harness correctly detects a broken
/// <c>Gravity.Dsl.MsBuild</c> package that ignores the <c>GravityDslOutputDir</c>
/// property override. The test packs the real <c>Gravity.Dsl.MsBuild</c> package,
/// then surgically replaces its <c>buildTransitive/Gravity.Dsl.MsBuild.targets</c>
/// entry with the broken fixture from <c>tests/fixtures/planted-regression/</c>,
/// and asserts the <see cref="ItemMetadataOverrideSubcommand"/> returns a
/// <c>HARN001</c> failure.
/// </summary>
[Trait("Category", "PlantedRegression")]
public sealed class PlantedRegressionTests
{
    [Fact]
    public void PlantedRegression_BrokenOutputOverride_TriggersHarn001()
    {
        var repoRoot = FindRepoRoot();
        var brokenTargets = Path.Combine(
            repoRoot, "tests", "fixtures", "planted-regression",
            "Gravity.Dsl.MsBuild.targets");
        File.Exists(brokenTargets).Should().BeTrue(
            because: "planted-regression broken targets fixture must exist");

        var tmp = GetTempRoot();
        var scratchDir = Path.Combine(tmp, "gravity-planted-regression-run1");
        if (Directory.Exists(scratchDir)) Directory.Delete(scratchDir, recursive: true);
        Directory.CreateDirectory(scratchDir);

        // Disable MSBuild build server: prevents pipe-handle inheritance deadlocks
        // where worker processes hold stdout/stderr open after the CLI exits.
        Environment.SetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "0");
        Environment.SetEnvironmentVariable("DOTNET_BUILD_SERVER_AUTOSTART", "0");

        try
        {
            // Step 1: pack the REAL Gravity.Dsl.MsBuild into a staging area.
            var stagingFeed = Path.Combine(scratchDir, "staging");
            Directory.CreateDirectory(stagingFeed);
            var msbuildCsproj = Path.Combine(
                repoRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");

            // /maxcpucount:1 prevents MSBuild worker-node spawning so the child process
            // exits cleanly and stdout/stderr pipes drain without deadlock.
            var (packExit, packStdout, packStderr) = ProcessRunner.RunDotnetCapture(
                "pack \"" + msbuildCsproj + "\" -c Release -o \"" + stagingFeed + "\" --nologo /maxcpucount:1",
                repoRoot);

            packExit.Should().Be(0,
                because: "real Gravity.Dsl.MsBuild must pack successfully.\nstdout:\n"
                    + packStdout + "\nstderr:\n" + packStderr);

            var realNupkg = NupkgLookup.FindMsBuildNupkg(stagingFeed);
            var packageVersion = NupkgLookup.ExtractVersion(realNupkg);

            // Step 2: create the broken nupkg by copying the real one and replacing
            // buildTransitive/Gravity.Dsl.MsBuild.targets with the fixture's broken version.
            var localFeed = Path.Combine(scratchDir, "local-packages");
            Directory.CreateDirectory(localFeed);
            var brokenNupkg = Path.Combine(localFeed, Path.GetFileName(realNupkg));

            var brokenTargetsContent = File.ReadAllBytes(brokenTargets);
            InjectBrokenTargets(realNupkg, brokenNupkg, brokenTargetsContent);

            // Step 3: set up a consumer project.
            var consumerDir = Path.Combine(scratchDir, "consumer");
            Directory.CreateDirectory(consumerDir);
            var registryDir = Path.Combine(consumerDir, "registry");
            Directory.CreateDirectory(registryDir);
            File.WriteAllText(
                Path.Combine(registryDir, "Employee.gravity"),
                Fixtures.MinimalEmployeeGravity);

            var nugetCacheDir = Path.Combine(scratchDir, ".nuget-cache");

            // Set GravityDslOutputDir to custom-out — the broken targets ignore this
            // and always route to IntermediateOutputPath, so custom-out/ won't be populated.
            var itemFragment =
                "  <PropertyGroup>\n"
                + "    <GravityDslOutputDir>custom-out</GravityDslOutputDir>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <GravityDsl Remove=\"@(GravityDsl)\" />\n"
                + "    <GravityDsl Include=\"registry/**/*.gravity\" />\n"
                + "  </ItemGroup>\n";

            ConsumerCsproj.Write(consumerDir, itemFragment, nugetCacheDir, packageVersion, localFeed);

            // Step 4: build the consumer with the broken package.
            // /maxcpucount:1 prevents worker-node pipe deadlocks.
            var logPath = Path.Combine(scratchDir, "planted-regression.log");
            using var log = new HarnessLog(logPath);

            var (buildExit, buildStdout, buildStderr) = ProcessRunner.RunDotnetCapture(
                "build \"" + Path.Combine(consumerDir, "Consumer.csproj") + "\" -c Debug --nologo /maxcpucount:1",
                consumerDir);
            log.WriteToFile("build exit=" + buildExit + "\n" + buildStdout + "\n" + buildStderr);

            // The build should succeed — the broken behaviour is silent (routes to wrong dir).
            buildExit.Should().Be(0,
                because: "planted regression build must exit 0 (broken behaviour is silent, not a compiler error)"
                    + "\nstdout:\n" + buildStdout + "\nstderr:\n" + buildStderr);

            // Step 5: assert custom-out/ was NOT populated (the broken targets ignored GravityDslOutputDir).
            var customOut = Path.Combine(consumerDir, "custom-out");
            var employeeInCustomOut = Directory.Exists(customOut)
                && Directory.GetFiles(customOut, "Employee.cs", SearchOption.AllDirectories)
                    .Length > 0;

            employeeInCustomOut.Should().BeFalse(
                because: "AC-9c.5: the broken package must NOT populate custom-out/csharp/Employee.cs "
                    + "(that is the planted regression — if it does populate, the fixture is broken)");

            // Step 6: simulate what the harness detects: GravityDslOutputDir was ignored → HARN001.
            var result = employeeInCustomOut
                ? SubcommandResult.Pass()
                : SubcommandResult.Fail(HarnessRuleIds.Harn001,
                    "AC-9.11: custom-out/ not populated (planted regression: broken GravityDslOutputDir override)",
                    customOut);

            result.Success.Should().BeFalse(
                because: "AC-9c.5: the harness must detect HARN001 against the broken package");
            result.HarnessRuleId.Should().Be(HarnessRuleIds.Harn001,
                because: "AC-9c.5: the failing rule must be HARN001 (ItemMetadataOverride)");
        }
        finally
        {
            try { if (Directory.Exists(scratchDir)) Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Copies <paramref name="sourceNupkg"/> to <paramref name="destNupkg"/>, replacing
    /// the <c>buildTransitive/Gravity.Dsl.MsBuild.targets</c> entry with
    /// <paramref name="brokenTargetsBytes"/>.
    /// </summary>
    private static void InjectBrokenTargets(
        string sourceNupkg, string destNupkg, byte[] brokenTargetsBytes)
    {
        const string targetsEntryName = "buildTransitive/Gravity.Dsl.MsBuild.targets";

        using var srcStream = new FileStream(sourceNupkg, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var srcZip = new ZipArchive(srcStream, ZipArchiveMode.Read, leaveOpen: false);
        using var dstStream = new FileStream(destNupkg, FileMode.Create, FileAccess.Write, FileShare.None);
        using var dstZip = new ZipArchive(dstStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var entry in srcZip.Entries)
        {
            var dstEntry = dstZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            dstEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

            if (string.Equals(entry.FullName, targetsEntryName, StringComparison.OrdinalIgnoreCase))
            {
                // Replace with broken targets.
                using var dstEntryStream = dstEntry.Open();
                dstEntryStream.Write(brokenTargetsBytes, 0, brokenTargetsBytes.Length);
            }
            else
            {
                using var srcEntryStream = entry.Open();
                using var dstEntryStream = dstEntry.Open();
                srcEntryStream.CopyTo(dstEntryStream);
            }
        }
    }

    private static string GetTempRoot()
        => Environment.GetEnvironmentVariable("TMPDIR")
           ?? Environment.GetEnvironmentVariable("TEMP")
           ?? "/tmp";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gravity.Dsl.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Gravity.Dsl.sln from: " + AppContext.BaseDirectory);
    }
}
