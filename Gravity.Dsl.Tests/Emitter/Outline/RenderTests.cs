using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.Sample.Outline;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.Outline;

/// <summary>
/// Structural assertions on the Markdown the outline emitter produces (AC-9.6).
/// TODO: byte-checked goldens for samples/registry entities — deferred because
/// the outline emitter is a non-production sample (LD-12) and the rendering
/// shape is intentionally allowed to evolve as the IEmitter contract grows.
/// </summary>
public sealed class OutlineEmitterRenderTests
{
    private static EmitterConfig OutlineConfig()
    {
        return new EmitterConfig(
            TargetName: "outline",
            Enabled: true,
            Output: "outline",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "outline"));
    }

    [Fact]
    public void Emit_AgainstRegistrySamples_ProducesAtLeastOneEntityMarkdown()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        var result = new OutlineEmitter().Emit(model, OutlineConfig(), sink);

        result.Diagnostics.Should().BeEmpty();
        var files = sink.Snapshot();
        files.Should().NotBeEmpty(because: "every registry entity emits a .md");
        files.Keys.Should().Contain(k => k.EndsWith("Employee.md", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Emit_EveryEntityFile_ContainsAllSixH2Sections()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new OutlineEmitter().Emit(model, OutlineConfig(), sink);

        // Identify entity files: the outline emitter writes one .md per entity AND
        // per value type / enum, but only entity files carry all six H2 sections.
        // Names match the AST: Employee, TimeEntry, Project are entities in the
        // canonical registry sample.
        var entityFiles = sink.Snapshot()
            .Where(kv => kv.Key.EndsWith("Employee.md", System.StringComparison.Ordinal)
                      || kv.Key.EndsWith("TimeEntry.md", System.StringComparison.Ordinal)
                      || kv.Key.EndsWith("Project.md", System.StringComparison.Ordinal))
            .ToList();

        entityFiles.Should().NotBeEmpty(because: "registry must contain entities Employee, TimeEntry, Project");
        foreach (var (path, body) in entityFiles)
        {
            body.Should().Contain("## Identity", because: path + " must carry the Identity section");
            body.Should().Contain("## Relations", because: path + " must carry the Relations section");
            body.Should().Contain("## Properties", because: path + " must carry the Properties section");
            body.Should().Contain("## Lifecycle", because: path + " must carry the Lifecycle section");
            body.Should().Contain("## Events", because: path + " must carry the Events section");
            body.Should().Contain("## Commands", because: path + " must carry the Commands section");
        }
    }

    [Fact]
    public void Emit_EntityHeader_NamesEntityAndVersion()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new OutlineEmitter().Emit(model, OutlineConfig(), sink);

        var employee = sink.Snapshot().FirstOrDefault(
            kv => kv.Key.EndsWith("Employee.md", System.StringComparison.Ordinal));
        employee.Value.Should().NotBeNull();
        // H1 of the form "# Employee@N"
        employee.Value.Should().StartWith("# Employee@");
    }

    [Fact]
    public void Emit_UsesLfLineEndings()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new OutlineEmitter().Emit(model, OutlineConfig(), sink);

        foreach (var kv in sink.Snapshot())
        {
            kv.Value.Should().NotContain("\r",
                because: kv.Key + " must use LF line endings only (FR-222)");
        }
    }
}
