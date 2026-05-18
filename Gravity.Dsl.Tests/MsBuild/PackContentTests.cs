using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.MsBuild;

/// <summary>
/// AC-9.1 — packs <c>Gravity.Dsl.MsBuild</c> to a temp directory via a child
/// <c>dotnet pack</c> process, then asserts the resulting <c>.nupkg</c> contains
/// the documented entries under <c>buildTransitive/</c> and <c>tasks/net9.0/</c>
/// and explicitly does <em>not</em> bundle <c>Microsoft.Build.Utilities.Core.dll</c>
/// (FR-210; MSBuild's own ALC provides that assembly).
/// </summary>
public sealed class PackContentTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public void Pack_ProducesExpectedLayout()
    {
        var repoRoot = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.MsBuild");
        var projectPath = Path.Combine(repoRoot, "Gravity.Dsl.MsBuild.csproj");
        File.Exists(projectPath).Should().BeTrue(because: "Gravity.Dsl.MsBuild.csproj must exist");

        var tempRoot = Path.Combine(Path.GetTempPath(),
            "gravity-pack-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            RunDotnetPack(projectPath, tempRoot);

            var nupkg = Directory.GetFiles(tempRoot, "Gravity.Dsl.MsBuild.*.nupkg").FirstOrDefault();
            nupkg.Should().NotBeNull(because: "dotnet pack must produce a .nupkg under " + tempRoot);

            using var archive = ZipFile.OpenRead(nupkg!);
            var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(System.StringComparer.Ordinal);

            // Required build-asset files.
            names.Should().Contain("buildTransitive/Gravity.Dsl.MsBuild.props");
            names.Should().Contain("buildTransitive/Gravity.Dsl.MsBuild.targets");

            // Required task assembly + the full CompilerPipeline closure under tasks/net9.0/.
            names.Should().Contain("tasks/net9.0/Gravity.Dsl.MsBuild.dll");
            names.Should().Contain("tasks/net9.0/Gravity.Dsl.Ast.dll");
            names.Should().Contain("tasks/net9.0/Gravity.Dsl.Compiler.dll");
            names.Should().Contain("tasks/net9.0/Gravity.Dsl.Emitter.dll");
            names.Should().Contain("tasks/net9.0/Gravity.Dsl.Emitter.CSharp.dll");
            names.Should().Contain("tasks/net9.0/Pidgin.dll");
            names.Should().Contain("tasks/net9.0/YamlDotNet.dll");
            names.Should().Contain("tasks/net9.0/gravc.dll");

            // Roslyn workspaces assembly. The exact file name flows from the NuGet pin
            // and may carry a culture suffix on some hosts; assert any 'Microsoft.CodeAnalysis.CSharp.Workspaces' DLL exists.
            names.Any(n => n == "tasks/net9.0/Microsoft.CodeAnalysis.CSharp.Workspaces.dll")
                .Should().BeTrue(because: "Roslyn C# workspaces are part of the CompilerPipeline closure");

            // ABSENT: Microsoft.Build.Utilities.Core.dll — MSBuild's own ALC provides this assembly.
            names.Any(n => n.EndsWith("/Microsoft.Build.Utilities.Core.dll", System.StringComparison.Ordinal))
                .Should().BeFalse(
                    because: "ExcludeAssets=\"runtime\" on the PackageReference must keep Microsoft.Build.Utilities.Core out of tasks/net9.0/ (FR-210, AC-9.1)");

            // ABSENT: any entry under build/ that is not a buildTransitive/ path. FR-210 pins
            // the buildTransitive/ layout exclusively; a legacy build/ mirror would shadow
            // buildTransitive/ on consumers that resolve project references differently and
            // is exactly the failure mode FR-210 forbids.
            names.Any(n => n.StartsWith("build/", System.StringComparison.Ordinal)
                        && !n.StartsWith("buildTransitive/", System.StringComparison.Ordinal))
                .Should().BeFalse(because: "FR-210 — no legacy build/ mirror; buildTransitive/ only");
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    internal static void RunDotnetPack(string projectPath, string outputDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "pack \"" + projectPath + "\" --output \"" + outputDir + "\" -c Debug -p:Version=0.1.0-test --nologo",
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
        if (!p.WaitForExit(180_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new System.InvalidOperationException("dotnet pack timed out");
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        p.ExitCode.Should().Be(0,
            because: "dotnet pack must succeed.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
    }
}
