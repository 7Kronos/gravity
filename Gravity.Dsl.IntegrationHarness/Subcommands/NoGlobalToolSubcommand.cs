using System;
using System.IO;
using System.Linq;
using Gravity.Dsl.IntegrationHarness.Shared;

namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// AC-9.14: the MSBuild flow does not require <c>gravc</c> as a global .NET tool.
/// Step 1 asserts <c>dotnet tool list -g</c> does not list gravc. Step 2 builds
/// the smoke consumer and confirms codegen ran (FR-3013).
/// </summary>
public sealed class NoGlobalToolSubcommand : ISubcommand
{
    /// <inheritdoc/>
    public string SubcommandName => "run-ac-9.14";

    /// <inheritdoc/>
    public string AcId => "9.14";

    /// <inheritdoc/>
    public SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log)
    {
        log.WriteToFile("[NoGlobalTool] starting; scratchDir=" + scratchDir);

        // Step 1: assert gravc is NOT installed as a global tool.
        var (toolExit, toolStdout, toolStderr) = ProcessRunner.RunDotnetCapture(
            "tool list -g", workspaceRoot);
        log.WriteToFile("tool list exit=" + toolExit + "\n" + toolStdout + "\n" + toolStderr);

        if (toolExit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn004,
                "`dotnet tool list -g` failed with exit " + toolExit, null, toolExit);

        // Line-by-line check: leading whitespace-trimmed token must not equal "gravc".
        var lines = toolStdout.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("gravc", StringComparison.Ordinal)
                && (trimmed.Length == 5 || trimmed[5] == ' ' || trimmed[5] == '\t'))
            {
                return SubcommandResult.Fail(HarnessRuleIds.Harn004,
                    "AC-9.14: gravc is installed as a global tool. "
                    + "Remediation: run `dotnet tool uninstall -g gravc` before re-running the harness.");
            }
        }

        // Step 2: pack and build the canonical consumer.
        var localFeed = Path.Combine(scratchDir, "local-packages");
        Directory.CreateDirectory(localFeed);
        var msbuildCsproj = Path.Combine(
            workspaceRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");

        log.WriteToFile("[NoGlobalTool] packing Gravity.Dsl.MsBuild");
        var (packExit, packStdout, packStderr) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c Release -o \"" + localFeed + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("pack exit=" + packExit + "\n" + packStdout + "\n" + packStderr);
        if (packExit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn004,
                "dotnet pack failed with exit " + packExit, localFeed, packExit);

        var nupkgFiles = Directory.GetFiles(localFeed, "Gravity.Dsl.MsBuild.*.nupkg");
        if (nupkgFiles.Length == 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn004,
                "No Gravity.Dsl.MsBuild .nupkg found in " + localFeed);
        var packageVersion = ExtractVersion(nupkgFiles[0]);

        var consumerDir = Path.Combine(scratchDir, "consumer");
        Directory.CreateDirectory(consumerDir);
        var registryDir = Path.Combine(consumerDir, "registry");
        Directory.CreateDirectory(registryDir);
        File.WriteAllText(Path.Combine(registryDir, "Employee.gravity"), Fixtures.MinimalEmployeeGravity);

        var nugetCacheDir = Path.Combine(scratchDir, ".nuget-cache");
        ConsumerCsproj.Write(consumerDir, string.Empty, nugetCacheDir, packageVersion, localFeed);

        log.WriteToFile("[NoGlobalTool] running dotnet build");
        var (exit, stdout, stderr) = ProcessRunner.RunDotnetCapture(
            "build \"" + Path.Combine(consumerDir, "Consumer.csproj") + "\" -c Debug --nologo",
            consumerDir);
        log.WriteToFile("build exit=" + exit + "\n" + stdout + "\n" + stderr);

        if (exit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn004,
                "AC-9.14: smoke build failed with exit " + exit, consumerDir, exit);

        // Confirm codegen ran — Employee.cs under obj/Generated/.../csharp/
        var generated = Directory
            .GetFiles(consumerDir, "Employee.cs", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.DirectorySeparatorChar + "csharp" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        if (generated.Count == 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn004,
                "AC-9.14: no generated Employee.cs found under csharp/ — codegen did not run",
                consumerDir);

        log.WriteToFile("[NoGlobalTool] PASS");
        return SubcommandResult.Pass();
    }

    private static string ExtractVersion(string nupkgPath)
    {
        var filename = Path.GetFileNameWithoutExtension(nupkgPath);
        var prefix = "Gravity.Dsl.MsBuild.";
        return filename.StartsWith(prefix, StringComparison.Ordinal)
            ? filename.Substring(prefix.Length)
            : "0.1.0";
    }
}
