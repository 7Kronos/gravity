using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Tests.Helpers;

/// <summary>
/// Shared helper for tests that need to load <c>samples/registry/</c> as a
/// <see cref="ResolvedModel"/>. Walks upward from the test assembly base
/// directory until <c>samples/registry/</c> is located.
/// </summary>
internal static class SamplesLoader
{
    public static string FindRepoSubdirectory(string relative)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate '" + relative + "' from test base directory");
    }

    public static string RegistrySamplesDir() => FindRepoSubdirectory(Path.Combine("samples", "registry"));

    public static string GoldenCSharpDir() => FindRepoSubdirectory(Path.Combine("tests", "golden", "csharp"));

    public static string GoldenOutlineDir() => FindRepoSubdirectory(Path.Combine("tests", "golden", "outline"));

    public static ResolvedModel LoadRegistry()
    {
        var registryDir = RegistrySamplesDir();
        var sources = Directory.GetFiles(registryDir, "*.gravity", SearchOption.TopDirectoryOnly);
        System.Array.Sort(sources, System.StringComparer.Ordinal);
        var files = new List<SourceFile>();
        foreach (var src in sources)
        {
            var parsed = Parser.Parse(src, File.ReadAllText(src));
            parsed.Diagnostics.Should().BeEmpty(because: "samples must parse cleanly: " + src);
            files.Add(parsed.File!);
        }
        var resolve = Resolver.Resolve(files, registryDir);
        resolve.Model.Should().NotBeNull();
        return resolve.Model!;
    }
}
