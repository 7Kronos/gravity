using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.CSharp;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.CSharp;

/// <summary>
/// AC-6b: running the C# emitter twice in-process against the registry samples
/// produces byte-identical output.
/// </summary>
public sealed class CSharpDeterminismTests
{
    [Fact]
    public async Task TwoRuns_AreByteIdentical()
    {
        var model = SamplesLoader.LoadRegistry();
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

        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new CSharpEmitter() });
        var first = await EmitterHost.Run(model, configs, registry, outputRoot: null);
        var second = await EmitterHost.Run(model, configs, registry, outputRoot: null);

        first.Diagnostics.Should().BeEmpty();
        second.Diagnostics.Should().BeEmpty();

        var a = first.EmitterBuffers["csharp"].Snapshot();
        var b = second.EmitterBuffers["csharp"].Snapshot();

        a.Keys.Should().Equal(b.Keys, because: "file set must be identical across runs");
        foreach (var key in a.Keys)
        {
            a[key].Should().Be(b[key], because: "file '" + key + "' must be byte-identical across runs");
        }
    }
}
