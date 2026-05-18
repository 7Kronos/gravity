using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.CSharp;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.CSharp;

/// <summary>
/// AC-2: the C# emitter must produce byte-identical output to a checked-in
/// golden tree under <c>tests/golden/csharp/</c>. Every file in the golden tree
/// must be produced by the emitter, and every emitter-produced file must match
/// its golden byte-for-byte (LF, UTF-8 no BOM).
/// </summary>
public sealed class GoldenFileTests
{
    private static async Task<ImmutableSortedDictionary<string, string>> RunCSharpEmitter()
    {
        var model = SamplesLoader.LoadRegistry();
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new CSharpEmitter() });
        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["csharp"] = new EmitterConfig(
                TargetName: "csharp",
                Enabled: true,
                Output: "gen/csharp",
                Values: ImmutableSortedDictionary<string, object>.Empty
                    .Add("output", "gen/csharp")
                    .Add("namespace", "AcmeCo.Domain")
                    .Add("file_scoped_namespaces", true))
        };
        var run = await EmitterHost.Run(model, configs, registry, outputRoot: null);
        run.Diagnostics.Should().BeEmpty(because: "csharp emitter must produce zero diagnostics on the registry sample");
        return run.EmitterBuffers["csharp"].Snapshot();
    }

    [Fact]
    public async Task EveryGoldenFile_IsProducedByTheEmitter_AndMatchesByteForByte()
    {
        var goldenRoot = SamplesLoader.GoldenCSharpDir();
        var goldens = Directory.GetFiles(goldenRoot, "*.cs", SearchOption.AllDirectories);
        goldens.Should().NotBeEmpty(because: "tests/golden/csharp must be populated");

        var emitted = await RunCSharpEmitter();

        foreach (var goldenPath in goldens)
        {
            // Golden relative path under tests/golden/csharp; emitter writes under
            // its configured output ("gen/csharp/<dir>/<file>.cs"), so we expect
            // the buffer key to be "gen/csharp/<rel>".
            var rel = Path.GetRelativePath(goldenRoot, goldenPath).Replace('\\', '/');
            emitted.Keys.Should().Contain(rel, because: "golden file '" + rel + "' must be produced");
            var goldenText = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            emitted[rel].Should().Be(goldenText, because: "AC-2: golden '" + rel + "' must match emitter output");
        }
    }

    [Fact]
    public async Task EmitterProducesNoExtraFiles_BeyondTheGoldens()
    {
        var goldenRoot = SamplesLoader.GoldenCSharpDir();
        var goldens = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var g in Directory.GetFiles(goldenRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(goldenRoot, g).Replace('\\', '/');
            goldens.Add(rel);
        }
        var emitted = await RunCSharpEmitter();
        var extras = emitted.Keys.Where(k => !goldens.Contains(k)).ToArray();
        extras.Should().BeEmpty(because: "emitter must not produce files outside the locked golden set");
    }
}
