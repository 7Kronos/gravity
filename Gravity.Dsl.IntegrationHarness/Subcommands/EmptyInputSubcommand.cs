using System;
using System.IO;
using Gravity.Dsl.IntegrationHarness.Shared;

namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// AC-9.13: a consumer with zero <c>.gravity</c> files builds cleanly. The
/// target's <c>Condition="'@(GravityDsl)' != ''"</c> keeps GravityDslGenerate
/// dormant — no diagnostics, no generated artefacts (FR-3012).
/// </summary>
public sealed class EmptyInputSubcommand : ISubcommand
{
    private static readonly string[] DiagnosticPrefixes =
        { "PARSE", "VAL", "RES", "LEX", "HOST", "MSB", "JS", "CFG" };

    /// <inheritdoc/>
    public string SubcommandName => "run-ac-9.13";

    /// <inheritdoc/>
    public string AcId => "9.13";

    /// <inheritdoc/>
    public SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log, string config)
    {
        log.WriteToFile("[EmptyInput] starting; scratchDir=" + scratchDir + " config=" + config);

        var localFeed = Path.Combine(scratchDir, "local-packages");
        Directory.CreateDirectory(localFeed);
        var msbuildCsproj = Path.Combine(
            workspaceRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");

        log.WriteToFile("[EmptyInput] packing Gravity.Dsl.MsBuild -c " + config);
        var (packExit, packStdout, packStderr) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c " + config + " -o \"" + localFeed + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("pack exit=" + packExit + "\n" + packStdout + "\n" + packStderr);
        if (packExit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn003,
                "dotnet pack failed with exit " + packExit, localFeed, packExit);

        string packageVersion;
        try
        {
            packageVersion = NupkgLookup.ExtractVersion(NupkgLookup.FindMsBuildNupkg(localFeed));
        }
        catch (InvalidOperationException ex)
        {
            return SubcommandResult.Fail(HarnessRuleIds.Harn003, ex.Message);
        }

        // Deliberately do NOT create any .gravity files.
        var consumerDir = Path.Combine(scratchDir, "consumer");
        Directory.CreateDirectory(consumerDir);
        var nugetCacheDir = Path.Combine(scratchDir, ".nuget-cache");
        ConsumerCsproj.Write(consumerDir, string.Empty, nugetCacheDir, packageVersion, localFeed);

        log.WriteToFile("[EmptyInput] running dotnet build");
        var (exit, stdout, stderr) = ProcessRunner.RunDotnetCapture(
            "build \"" + Path.Combine(consumerDir, "Consumer.csproj") + "\" -c Debug --nologo",
            consumerDir);
        log.WriteToFile("build exit=" + exit + "\n" + stdout + "\n" + stderr);

        // (a) exit 0
        if (exit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn003,
                "AC-9.13: empty input must build cleanly; exit=" + exit, consumerDir, exit);

        // (b) no diagnostic-id substrings from the Gravity DSL toolchain
        foreach (var prefix in DiagnosticPrefixes)
        {
            if (stdout.Contains(": error " + prefix, StringComparison.Ordinal))
                return SubcommandResult.Fail(HarnessRuleIds.Harn003,
                    "AC-9.13: unexpected error diagnostic with prefix " + prefix + " in stdout",
                    consumerDir);
            if (stdout.Contains(": warning " + prefix, StringComparison.Ordinal))
                return SubcommandResult.Fail(HarnessRuleIds.Harn003,
                    "AC-9.13: unexpected warning diagnostic with prefix " + prefix + " in stdout",
                    consumerDir);
        }

        // (c) no obj/Generated/ directory
        var objDir = Path.Combine(consumerDir, "obj");
        if (Directory.Exists(objDir))
        {
            var generatedDirs = Directory.GetDirectories(objDir, "Generated", SearchOption.AllDirectories);
            if (generatedDirs.Length > 0)
                return SubcommandResult.Fail(HarnessRuleIds.Harn003,
                    "AC-9.13: obj/Generated/ directory exists despite empty input",
                    generatedDirs[0]);
        }

        log.WriteToFile("[EmptyInput] PASS");
        return SubcommandResult.Pass();
    }
}
