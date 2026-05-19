using System;
using System.Collections.Immutable;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.JsonSchema;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.9 — running the JSON Schema emitter twice in-process against the
/// registry samples produces byte-identical output buffers.
/// </summary>
public sealed class DeterminismTests
{
    [Fact]
    public void TwoRuns_AreByteIdentical()
    {
        var model = SamplesLoader.LoadRegistry();
        var cfg = new EmitterConfig(
            TargetName: "json-schema", Enabled: true, Output: "json-schema",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "json-schema"));

        var firstSink = new BufferedEmitterOutput();
        var first = new JsonSchemaEmitter().Emit(model, cfg, firstSink);
        first.Diagnostics.Should().BeEmpty();
        var firstSnap = firstSink.Snapshot();

        var secondSink = new BufferedEmitterOutput();
        var second = new JsonSchemaEmitter().Emit(model, cfg, secondSink);
        second.Diagnostics.Should().BeEmpty();
        var secondSnap = secondSink.Snapshot();

        firstSnap.Keys.Should().Equal(secondSnap.Keys,
            because: "file set must be identical across runs (AC-4.9)");
        foreach (var key in firstSnap.Keys)
        {
            firstSnap[key].Should().Be(secondSnap[key],
                because: "file '" + key + "' must be byte-identical across runs");
        }
    }
}
