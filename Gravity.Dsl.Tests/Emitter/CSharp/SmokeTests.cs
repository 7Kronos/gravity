using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Cli;
using Gravity.Dsl.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.CSharp;

/// <summary>
/// AC-3 end-to-end smoke test: drive the CLI's gen workflow as a library, then
/// load every emitted <c>.cs</c> file into an in-process Roslyn compilation and
/// assert zero compilation errors. The test references the standard .NET 9
/// reference assemblies plus <see cref="System.Collections.Immutable"/> so
/// <c>ImmutableArray&lt;T&gt;</c> resolves.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public async Task GenCommand_ProducesCompilableCSharp()
    {
        var inputRoot = SamplesLoader.RegistrySamplesDir();
        var outputRoot = Path.Combine(
            Path.GetDirectoryName(typeof(SmokeTests).Assembly.Location)!,
            "smoke-csharp-" + Interlocked.Increment(ref _seq));
        try
        {
            var result = await CompilerPipeline.Gen(inputRoot, outputRoot, default(System.DateOnly), emitterFilter: new[] { "csharp" });
            result.Success.Should().BeTrue(
                because: "gen should succeed on the registry samples; got: "
                    + string.Join("; ", result.Diagnostics.Select(d => d.RuleId + " " + d.Message)));

            var csFiles = Directory.GetFiles(outputRoot, "*.cs", SearchOption.AllDirectories);
            csFiles.Should().NotBeEmpty(because: "gen must write generated .cs files");

            var trees = csFiles
                .OrderBy(f => f, System.StringComparer.Ordinal)
                .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
                .ToList();

            var references = BuildReferences();
            var compilation = CSharpCompilation.Create(
                assemblyName: "GravityGenSmoke",
                syntaxTrees: trees,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));

            var diags = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToArray();
            diags.Should().BeEmpty(
                because: "generated C# must compile cleanly; first errors:\n"
                    + string.Join("\n", diags.Take(10).Select(d => d.ToString())));
        }
        finally
        {
            try { if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static int _seq;

    private static IEnumerable<MetadataReference> BuildReferences()
    {
        // Use the running runtime's reference assemblies. Each test runs against
        // net9.0 (TFM is fixed in Directory.Build.props), so we pull every assembly
        // that Microsoft ships in the shared framework's reference set.
        var refs = new List<MetadataReference>();

        // Core BCL types via the running runtime's TPA list. This is the most
        // portable approach across CI hosts.
        var trustedAssemblies = (System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty;
        var sep = System.OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var path in trustedAssemblies.Split(sep, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(path))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // Belt-and-braces: include ImmutableArray<T>'s assembly explicitly if it
        // wasn't already in TPA (it always is on net9.0 but the safeguard is cheap).
        var immAsm = typeof(ImmutableArray<int>).Assembly.Location;
        if (!string.IsNullOrEmpty(immAsm) && File.Exists(immAsm)
            && !refs.OfType<PortableExecutableReference>().Any(r => r.FilePath == immAsm))
        {
            refs.Add(MetadataReference.CreateFromFile(immAsm));
        }

        return refs;
    }
}
