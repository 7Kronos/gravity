using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
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
        var repoRoot = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.MsBuild");
        var msbuildProject = Path.Combine(repoRoot, "Gravity.Dsl.MsBuild.csproj");
        File.Exists(msbuildProject).Should().BeTrue();

        // Step 1. Pack Gravity.Dsl.MsBuild into a local-packages directory inside
        // the temp fixture. Both the .nupkg and the consumer csproj live under a
        // single self-contained tree so the test cleans up to a single rm -rf.
        var fixtureRoot = Path.Combine(Path.GetTempPath(),
            "gravity-smoke-" + System.Guid.NewGuid().ToString("N"));
        var localFeed = Path.Combine(fixtureRoot, "local-packages");
        var consumerDir = Path.Combine(fixtureRoot, "consumer");
        var consumerObj = Path.Combine(consumerDir, "obj");
        Directory.CreateDirectory(localFeed);
        Directory.CreateDirectory(consumerDir);
        Directory.CreateDirectory(Path.Combine(consumerDir, "registry"));

        try
        {
            // Pack with an explicit version so the consumer's PackageReference resolves.
            RunDotnet(
                "pack \"" + msbuildProject + "\" --output \"" + localFeed
                    + "\" -c Debug -p:Version=0.1.0-smoke --nologo",
                workingDir: repoRoot);

            // Step 2. Write the consumer csproj. Library output type so we do not need
            // an entry point; net9.0 matches the repo's pinned TFM.
            File.WriteAllText(Path.Combine(consumerDir, "Consumer.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                + "  <PropertyGroup>\n"
                + "    <TargetFramework>net9.0</TargetFramework>\n"
                + "    <OutputType>Library</OutputType>\n"
                + "    <RestorePackagesPath>" + Path.Combine(fixtureRoot, ".nuget-cache").Replace("\\", "\\\\") + "</RestorePackagesPath>\n"
                + "    <Nullable>enable</Nullable>\n"
                + "  </PropertyGroup>\n"
                + "  <ItemGroup>\n"
                + "    <PackageReference Include=\"Gravity.Dsl.MsBuild\" Version=\"0.1.0-smoke\" />\n"
                + "  </ItemGroup>\n"
                + "</Project>\n");

            // nuget.config: clear global feeds; point at the local-packages directory only.
            File.WriteAllText(Path.Combine(consumerDir, "nuget.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<configuration>\n"
                + "  <packageSources>\n"
                + "    <clear />\n"
                + "    <add key=\"gravity-smoke-local\" value=\"" + localFeed + "\" />\n"
                + "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />\n"
                + "  </packageSources>\n"
                + "</configuration>\n");

            // Minimal Gravity source — one entity with property + lifecycle (mirrors
            // the Phase 9 spike fixture at /tmp/phase9-spike/registry/Employee.gravity
            // verbatim so any divergence between this test and the spike is visible).
            File.WriteAllText(Path.Combine(consumerDir, "registry", "Employee.gravity"),
                "namespace hr;\n"
                + "\n"
                + "entity Employee version 1 {\n"
                + "\n"
                + "  identity id: UUID;\n"
                + "\n"
                + "  properties {\n"
                + "    name: String;\n"
                + "    email: String?;\n"
                + "  }\n"
                + "\n"
                + "  lifecycle {\n"
                + "    states {\n"
                + "      Active, Terminated;\n"
                + "    }\n"
                + "    transitions {\n"
                + "      Active -> Terminated on Terminated;\n"
                + "    }\n"
                + "  }\n"
                + "\n"
                + "  events {\n"
                + "    Terminated { terminated_at: DateTime; };\n"
                + "  }\n"
                + "\n"
                + "  commands {\n"
                + "    Terminate(reason: String)\n"
                + "      returns TerminationResult\n"
                + "      with side_effect Terminated;\n"
                + "  }\n"
                + "}\n"
                + "\n"
                + "type TerminationResult {\n"
                + "  success: Boolean;\n"
                + "  message: String?;\n"
                + "}\n");

            // Step 3. dotnet build the consumer. The GravityDslGenerate target runs
            // before CoreCompile, writes generated .cs under obj/Generated/csharp/,
            // and the Compile item include lifts them into the C# compilation.
            var (exitCode, stdout, stderr) = RunDotnetCapture(
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
                .Where(p => p.Contains(Path.DirectorySeparatorChar + "csharp" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
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

    private static (int ExitCode, string Stdout, string Stderr) RunDotnetCapture(string args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        // Read stdout / stderr concurrently to avoid the classic full-pipe-buffer
        // deadlock when the child emits more than ~4 KB to either stream before
        // we get to ReadToEnd on the other one.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(240_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new System.InvalidOperationException("dotnet timed out: " + args);
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return (p.ExitCode, stdout, stderr);
    }

    private static void RunDotnet(string args, string workingDir)
    {
        var (exit, stdout, stderr) = RunDotnetCapture(args, workingDir);
        exit.Should().Be(0,
            because: "dotnet " + args + " must succeed.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
    }
}
