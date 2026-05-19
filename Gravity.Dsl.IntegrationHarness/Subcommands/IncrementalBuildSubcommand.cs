using System;
using System.IO;
using Gravity.Dsl.IntegrationHarness.Shared;

namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// AC-9.15: two back-to-back <c>dotnet build</c> invocations against an
/// unchanged consumer fixture. The first must execute <c>GravityDslGenTask</c>;
/// the second must skip it via MSBuild's Inputs/Outputs short-circuit (FR-3014).
/// A third build after a deterministic source-file touch must re-run the task.
/// </summary>
public sealed class IncrementalBuildSubcommand : ISubcommand
{
    /// <inheritdoc/>
    public string SubcommandName => "run-ac-9.15";

    /// <inheritdoc/>
    public string AcId => "9.15";

    /// <inheritdoc/>
    public SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log, string config)
    {
        log.WriteToFile("[IncrementalBuild] starting; scratchDir=" + scratchDir + " config=" + config);

        var localFeed = Path.Combine(scratchDir, "local-packages");
        Directory.CreateDirectory(localFeed);
        var msbuildCsproj = Path.Combine(
            workspaceRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");

        log.WriteToFile("[IncrementalBuild] packing Gravity.Dsl.MsBuild -c " + config);
        var (packExit, packStdout, packStderr) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c " + config + " -o \"" + localFeed + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("pack exit=" + packExit + "\n" + packStdout + "\n" + packStderr);
        if (packExit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn005,
                "dotnet pack failed with exit " + packExit, localFeed, packExit);

        string packageVersion;
        try
        {
            packageVersion = NupkgLookup.ExtractVersion(NupkgLookup.FindMsBuildNupkg(localFeed));
        }
        catch (InvalidOperationException ex)
        {
            return SubcommandResult.Fail(HarnessRuleIds.Harn005, ex.Message);
        }

        var consumerDir = Path.Combine(scratchDir, "consumer");
        Directory.CreateDirectory(consumerDir);
        var registryDir = Path.Combine(consumerDir, "registry");
        Directory.CreateDirectory(registryDir);
        var gravitySource = Path.Combine(registryDir, "Employee.gravity");
        File.WriteAllText(gravitySource, Fixtures.MinimalEmployeeGravity);

        var nugetCacheDir = Path.Combine(scratchDir, ".nuget-cache");
        var csproj = ConsumerCsproj.Write(consumerDir, string.Empty, nugetCacheDir, packageVersion, localFeed);

        // Sub-step 1: first build with detailed verbosity.
        log.WriteToFile("[IncrementalBuild] sub-step 1: first build");
        var (exit1, stdout1, stderr1) = ProcessRunner.RunDotnetCapture(
            "build \"" + csproj + "\" -c Debug --nologo /verbosity:detailed",
            consumerDir);
        log.WriteToFile("build1 exit=" + exit1 + "\n" + stdout1 + "\n" + stderr1);

        if (exit1 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn005,
                "AC-9.15: first build failed with exit " + exit1, consumerDir, exit1);

        if (!stdout1.Contains("Task \"GravityDslGenTask\"", StringComparison.Ordinal))
            return SubcommandResult.Fail(HarnessRuleIds.Harn006,
                "AC-9.15: first build did not contain 'Task \"GravityDslGenTask\"' in detailed output",
                consumerDir);

        // Sub-step 2: second build — no source changes; task must be skipped.
        log.WriteToFile("[IncrementalBuild] sub-step 2: second build (incremental)");
        var (exit2, stdout2, stderr2) = ProcessRunner.RunDotnetCapture(
            "build \"" + csproj + "\" -c Debug --nologo /verbosity:detailed",
            consumerDir);
        log.WriteToFile("build2 exit=" + exit2 + "\n" + stdout2 + "\n" + stderr2);

        if (exit2 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn005,
                "AC-9.15: second build failed with exit " + exit2, consumerDir, exit2);

        if (stdout2.Contains("Task \"GravityDslGenTask\"", StringComparison.Ordinal))
            return SubcommandResult.Fail(HarnessRuleIds.Harn007,
                "AC-9.15: second build (no source change) still ran GravityDslGenTask — incremental short-circuit did not fire",
                consumerDir);

        // Sub-step 3: touch the .gravity source with a timestamp newer than the stamp file, then rebuild.
        // We locate the stamp file by searching recursively under consumerDir (the path varies by SDK
        // version: may be obj/Debug/net9.0/Generated/ or simply Generated/ depending on IntermediateOutputPath).
        // We read the stamp's LastWriteTimeUtc (filesystem read — not a banned clock call) and add 2 seconds.
        log.WriteToFile("[IncrementalBuild] sub-step 3: touch gravity source, third build");
        var stampFiles = Directory.GetFiles(consumerDir, ".gravity-stamp", SearchOption.AllDirectories);
        var stampMtime = stampFiles.Length > 0
            ? new FileInfo(stampFiles[0]).LastWriteTimeUtc
            : new FileInfo(csproj).LastWriteTimeUtc;
        File.SetLastWriteTimeUtc(gravitySource, stampMtime + TimeSpan.FromSeconds(2));
        log.WriteToFile("[IncrementalBuild] stamp=" + (stampFiles.Length > 0 ? stampFiles[0] : "not found")
            + " stampMtime=" + stampMtime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            + " set gravity source mtime to " + (stampMtime + TimeSpan.FromSeconds(2)).ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        var (exit3, stdout3, stderr3) = ProcessRunner.RunDotnetCapture(
            "build \"" + csproj + "\" -c Debug --nologo /verbosity:detailed",
            consumerDir);
        log.WriteToFile("build3 exit=" + exit3 + "\n" + stdout3 + "\n" + stderr3);

        if (exit3 != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn005,
                "AC-9.15: third build (after touch) failed with exit " + exit3, consumerDir, exit3);

        if (!stdout3.Contains("Task \"GravityDslGenTask\"", StringComparison.Ordinal))
            return SubcommandResult.Fail(HarnessRuleIds.Harn008,
                "AC-9.15: third build (after touch) did not re-run GravityDslGenTask",
                consumerDir);

        log.WriteToFile("[IncrementalBuild] PASS");
        return SubcommandResult.Pass();
    }
}
