using System.IO;
using Gravity.Dsl.IntegrationHarness.Shared;

namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// AC-9.12: <c>GravityDslGenerate</c> runs before <c>CoreCompile</c> so the
/// consumer's own C# code can reference a generated type. Compiling
/// <c>Program.cs</c> that names <c>hr.Employee</c> proves hook ordering (FR-3011).
/// </summary>
public sealed class HookOrderSubcommand : ISubcommand
{

    /// <inheritdoc/>
    public string SubcommandName => "run-ac-9.12";

    /// <inheritdoc/>
    public string AcId => "9.12";

    /// <inheritdoc/>
    public SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log, string config)
    {
        log.WriteToFile("[HookOrder] starting; scratchDir=" + scratchDir + " config=" + config);

        var localFeed = Path.Combine(scratchDir, "local-packages");
        Directory.CreateDirectory(localFeed);
        var msbuildCsproj = Path.Combine(
            workspaceRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");

        log.WriteToFile("[HookOrder] packing Gravity.Dsl.MsBuild -c " + config);
        var (packExit, packStdout, packStderr) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c " + config + " -o \"" + localFeed + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("pack exit=" + packExit + "\n" + packStdout + "\n" + packStderr);
        if (packExit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn002,
                "dotnet pack failed with exit " + packExit, localFeed, packExit);

        string packageVersion;
        try
        {
            packageVersion = NupkgLookup.ExtractVersion(NupkgLookup.FindMsBuildNupkg(localFeed));
        }
        catch (System.InvalidOperationException ex)
        {
            return SubcommandResult.Fail(HarnessRuleIds.Harn002, ex.Message);
        }

        var consumerDir = Path.Combine(scratchDir, "consumer");
        Directory.CreateDirectory(consumerDir);
        var registryDir = Path.Combine(consumerDir, "registry");
        Directory.CreateDirectory(registryDir);
        File.WriteAllText(Path.Combine(registryDir, "Employee.gravity"), Fixtures.MinimalEmployeeGravity);

        // Consumer source referencing the generated hr.Employee type — proves
        // GravityDslGenerate must run before CoreCompile.
        File.WriteAllText(
            Path.Combine(consumerDir, "Program.cs"),
            "namespace HookProbe;\n"
            + "internal static class Probe\n"
            + "{\n"
            + "    public static System.Type EmployeeType = typeof(hr.Employee);\n"
            + "}\n");

        var nugetCacheDir = Path.Combine(scratchDir, ".nuget-cache");
        ConsumerCsproj.Write(consumerDir, string.Empty, nugetCacheDir, packageVersion, localFeed);

        // Use /verbosity:normal so MSBuild emits target-execution lines like "GravityDslGenerate:".
        // Default verbosity suppresses these lines; normal verbosity is required to see them.
        log.WriteToFile("[HookOrder] running dotnet build /verbosity:normal");
        var (exit, stdout, stderr) = ProcessRunner.RunDotnetCapture(
            "build \"" + Path.Combine(consumerDir, "Consumer.csproj") + "\" -c Debug --nologo /verbosity:normal",
            consumerDir);
        log.WriteToFile("build exit=" + exit + "\n" + stdout + "\n" + stderr);

        // (a) exit 0
        if (exit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn002,
                "AC-9.12: build failed with exit " + exit
                + " (if GravityDslGenerate ran before CoreCompile this should not happen)",
                consumerDir, exit);

        // (b) bin/Debug/net9.0/Consumer.dll exists
        var dll = Path.Combine(consumerDir, "bin", "Debug", "net9.0", "Consumer.dll");
        if (!File.Exists(dll))
            return SubcommandResult.Fail(HarnessRuleIds.Harn002,
                "AC-9.12: Consumer.dll not found at expected path: " + dll, consumerDir);

        // (c) build log contains canonical GravityDslGenerate target-execution marker.
        // /verbosity:normal emits "GravityDslGenerate:" when the target runs.
        if (!stdout.Contains("GravityDslGenerate:", System.StringComparison.Ordinal))
        {
            return SubcommandResult.Fail(HarnessRuleIds.Harn002,
                "AC-9.12: build succeeded and Consumer.dll exists, but 'GravityDslGenerate:' "
                + "was not found in /verbosity:normal build output — hook ordering cannot be confirmed",
                consumerDir);
        }

        log.WriteToFile("[HookOrder] PASS");
        return SubcommandResult.Pass();
    }
}
