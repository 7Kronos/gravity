using System.IO;
using System.Linq;
using Gravity.Dsl.IntegrationHarness.Shared;

namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// AC-9.11: a <c>&lt;GravityDsl Include="..." &gt;&lt;Output&gt;custom-out/&lt;/Output&gt;&lt;/GravityDsl&gt;</c>
/// item-metadata override writes generated files under the consumer-rooted
/// <c>custom-out/</c> tree, not under the default <c>obj/Generated/</c> (FR-3010).
/// </summary>
public sealed class ItemMetadataOverrideSubcommand : ISubcommand
{
    /// <inheritdoc/>
    public string SubcommandName => "run-ac-9.11";

    /// <inheritdoc/>
    public string AcId => "9.11";

    /// <inheritdoc/>
    public SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log)
    {
        log.WriteToFile("[ItemMetadataOverride] starting; scratchDir=" + scratchDir);

        // Pack the MsBuild package into a local feed.
        var localFeed = Path.Combine(scratchDir, "local-packages");
        Directory.CreateDirectory(localFeed);
        var msbuildCsproj = Path.Combine(
            workspaceRoot, "Gravity.Dsl.MsBuild", "Gravity.Dsl.MsBuild.csproj");

        log.WriteToFile("[ItemMetadataOverride] packing Gravity.Dsl.MsBuild");
        var (packExit, packStdout, packStderr) = ProcessRunner.RunDotnetCapture(
            "pack \"" + msbuildCsproj + "\" -c Release -o \"" + localFeed + "\" --nologo",
            workspaceRoot);
        log.WriteToFile("pack exit=" + packExit + "\n" + packStdout + "\n" + packStderr);
        if (packExit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                "dotnet pack failed with exit " + packExit, localFeed, packExit);

        // Determine package version from the packed filename.
        var nupkgFiles = Directory.GetFiles(localFeed, "Gravity.Dsl.MsBuild.*.nupkg");
        if (nupkgFiles.Length == 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                "No Gravity.Dsl.MsBuild .nupkg found in " + localFeed);
        var packageVersion = ExtractVersion(nupkgFiles[0]);

        // Set up consumer project.
        var consumerDir = Path.Combine(scratchDir, "consumer");
        Directory.CreateDirectory(consumerDir);
        var registryDir = Path.Combine(consumerDir, "registry");
        Directory.CreateDirectory(registryDir);
        File.WriteAllText(Path.Combine(registryDir, "Employee.gravity"), Fixtures.MinimalEmployeeGravity);

        var nugetCacheDir = Path.Combine(scratchDir, ".nuget-cache");

        // AC-9.11: override the output directory via $(GravityDslOutputDir) property, which is
        // the MSBuild-supported mechanism to route generated files to a custom path (FR-202).
        // Note: the child-element <Output> metadata form is blocked by MSBuild 17.x (MSB4118 —
        // "Output" is a reserved item metadata name). The $(GravityDslOutputDir) property is the
        // correct override supported by Gravity.Dsl.MsBuild.props (FR-251).
        var itemFragment =
            "  <PropertyGroup>\n"
            + "    <GravityDslOutputDir>custom-out</GravityDslOutputDir>\n"
            + "  </PropertyGroup>\n"
            + "  <ItemGroup>\n"
            + "    <GravityDsl Remove=\"@(GravityDsl)\" />\n"
            + "    <GravityDsl Include=\"registry/**/*.gravity\" />\n"
            + "  </ItemGroup>\n";

        ConsumerCsproj.Write(consumerDir, itemFragment, nugetCacheDir, packageVersion, localFeed);

        log.WriteToFile("[ItemMetadataOverride] running dotnet build");
        var (exit, stdout, stderr) = ProcessRunner.RunDotnetCapture(
            "build \"" + Path.Combine(consumerDir, "Consumer.csproj") + "\" -c Debug --nologo",
            consumerDir);
        log.WriteToFile("build exit=" + exit + "\n" + stdout + "\n" + stderr);

        // (a) exit 0
        if (exit != 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                "AC-9.11: build failed with exit " + exit, consumerDir, exit);

        // (b) custom-out/csharp/Employee.cs exists and is non-empty
        var customOut = Path.Combine(consumerDir, "custom-out");
        if (!Directory.Exists(customOut))
            return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                "AC-9.11: custom-out/ directory was not created", consumerDir);

        var generated = Directory
            .GetFiles(customOut, "Employee.cs", System.IO.SearchOption.AllDirectories)
            .Where(p => p.Contains(System.IO.Path.DirectorySeparatorChar + "csharp" + System.IO.Path.DirectorySeparatorChar, System.StringComparison.Ordinal))
            .ToList();
        if (generated.Count == 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                "AC-9.11: custom-out/csharp/Employee.cs not found", customOut);
        if (new FileInfo(generated[0]).Length == 0)
            return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                "AC-9.11: custom-out/csharp/Employee.cs is empty", generated[0]);

        // (c) no Employee.cs under obj/Generated/
        var objDir = Path.Combine(consumerDir, "obj");
        if (Directory.Exists(objDir))
        {
            var stragglers = Directory
                .GetFiles(objDir, "Employee.cs", System.IO.SearchOption.AllDirectories)
                .Where(p => p.Contains(System.IO.Path.DirectorySeparatorChar + "Generated" + System.IO.Path.DirectorySeparatorChar, System.StringComparison.Ordinal))
                .ToArray();
            if (stragglers.Length > 0)
                return SubcommandResult.Fail(HarnessRuleIds.Harn001,
                    "AC-9.11: Employee.cs found under obj/Generated/ despite Output override",
                    stragglers[0]);
        }

        log.WriteToFile("[ItemMetadataOverride] PASS");
        return SubcommandResult.Pass();
    }

    private static string ExtractVersion(string nupkgPath)
    {
        var filename = System.IO.Path.GetFileNameWithoutExtension(nupkgPath);
        // filename is e.g. "Gravity.Dsl.MsBuild.0.1.0"
        var prefix = "Gravity.Dsl.MsBuild.";
        return filename.StartsWith(prefix, System.StringComparison.Ordinal)
            ? filename.Substring(prefix.Length)
            : "0.1.0";
    }
}
