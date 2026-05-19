using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.1 (pack half) + FR-360 / FR-363 — packs
/// <c>Gravity.Dsl.Emitter.JsonSchema</c> via a child <c>dotnet pack</c> process
/// and asserts the resulting <c>.nupkg</c> contains exactly the documented
/// entries and does NOT smuggle in transitive dependencies the emitter
/// shouldn't ship.
/// </summary>
public sealed class PackContentTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public void Pack_ProducesExpectedLayout()
    {
        var repoRoot = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.Emitter.JsonSchema");
        var projectPath = Path.Combine(repoRoot, "Gravity.Dsl.Emitter.JsonSchema.csproj");
        File.Exists(projectPath).Should().BeTrue();

        var tempRoot = Path.Combine(Path.GetDirectoryName(typeof(PackContentTests).Assembly.Location)!,
            "pack-jsonschema-" + System.Threading.Interlocked.Increment(ref _seq));
        Directory.CreateDirectory(tempRoot);
        try
        {
            Gravity.Dsl.Tests.MsBuild.PackContentTests.RunDotnetPack(projectPath, tempRoot);

            var nupkg = Directory.GetFiles(tempRoot, "Gravity.Dsl.Emitter.JsonSchema.*.nupkg").FirstOrDefault();
            nupkg.Should().NotBeNull(because: "dotnet pack must produce a .nupkg under " + tempRoot);

            using var archive = ZipFile.OpenRead(nupkg!);
            var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/'))
                .ToHashSet(System.StringComparer.Ordinal);

            // Required content: the emitter DLL plus the buildTransitive props.
            names.Should().Contain("lib/net9.0/Gravity.Dsl.Emitter.JsonSchema.dll");
            names.Should().Contain("buildTransitive/Gravity.Dsl.Emitter.JsonSchema.props");

            // ABSENT: tasks/ — the emitter is loaded by EmitterRegistry.Discover
            // at host runtime, not as a build task itself (FR-360).
            names.Any(n => n.StartsWith("tasks/", System.StringComparison.Ordinal))
                .Should().BeFalse(because: "FR-360: no tasks/ tree");

            // ABSENT: tools/.
            names.Any(n => n.StartsWith("tools/", System.StringComparison.Ordinal))
                .Should().BeFalse(because: "FR-360: no tools/ tree");

            // ABSENT: compiler / CLI / unrelated assemblies (FR-300, FR-363).
            names.Any(n => n.EndsWith("/Gravity.Dsl.Compiler.dll", System.StringComparison.Ordinal))
                .Should().BeFalse(because: "FR-300: emitter does not depend on the compiler");
            names.Any(n => n.EndsWith("/Gravity.Dsl.Cli.dll", System.StringComparison.Ordinal))
                .Should().BeFalse();
            names.Any(n => n.EndsWith("/gravc.dll", System.StringComparison.Ordinal)).Should().BeFalse();
            names.Any(n => n.EndsWith("/JsonSchema.Net.dll", System.StringComparison.Ordinal))
                .Should().BeFalse(because: "FR-363: emitter has no JsonSchema.Net runtime dep");
            names.Any(n => n.EndsWith("/Gravity.Dsl.Emitter.CSharp.dll", System.StringComparison.Ordinal))
                .Should().BeFalse();
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static int _seq;
}
