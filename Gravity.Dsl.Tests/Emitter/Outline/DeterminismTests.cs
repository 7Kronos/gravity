using System.Collections.Immutable;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.Sample.Outline;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.Outline;

/// <summary>
/// AC-9.7 first half: running <see cref="OutlineEmitter"/> twice in-process
/// against the canonical registry samples produces byte-identical output.
/// </summary>
public sealed class OutlineEmitterDeterminismTests
{
    [Fact]
    public void TwoRuns_AreByteIdentical()
    {
        var model = SamplesLoader.LoadRegistry();
        var config = new EmitterConfig(
            TargetName: "outline",
            Enabled: true,
            Output: "outline",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "outline"));

        var emitter = new OutlineEmitter();

        var first = new BufferedEmitterOutput();
        emitter.Emit(model, config, first).Diagnostics.Should().BeEmpty();

        var second = new BufferedEmitterOutput();
        emitter.Emit(model, config, second).Diagnostics.Should().BeEmpty();

        var a = first.Snapshot();
        var b = second.Snapshot();

        a.Keys.Should().Equal(b.Keys, because: "file set must be identical across runs");
        foreach (var key in a.Keys)
        {
            a[key].Should().Be(b[key], because: "file '" + key + "' must be byte-identical across runs");
        }
    }
}
