using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.CSharp;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.CSharp;

/// <summary>
/// The <c>emit_events</c> config toggle controls whether per-entity
/// <c>&lt;Entity&gt;Events.cs</c> files are produced. When <c>false</c> the events
/// file is suppressed so consumers can supply their own event types, while the
/// entity record, State enum, and commands are left untouched. The default
/// (key absent / <c>true</c>) keeps emitting the events file.
/// </summary>
public sealed class EmitEventsToggleTests
{
    private static async Task<IReadOnlyList<string>> EmitFileNames(bool? emitEvents)
    {
        const string src =
@"namespace hr;
entity Worker version 1 {
  identity id: UUID;
  properties {
    name: String;
  }
  lifecycle {
    states { Active; }
    transitions {}
  }
  events {
    Hired { occurred_at: DateTime; };
  }
  commands {}
}";
        var parsed = Parser.Parse("Fixture.gravity", src);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull();

        var values = ImmutableSortedDictionary<string, object>.Empty
            .Add("output", "gen/csharp")
            .Add("namespace", "AcmeCo.Domain")
            .Add("file_scoped_namespaces", true);
        if (emitEvents.HasValue)
        {
            values = values.Add("emit_events", emitEvents.Value);
        }

        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["csharp"] = new EmitterConfig(
                TargetName: "csharp", Enabled: true, Output: "gen/csharp",
                Values: values),
        };
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new CSharpEmitter() });
        var run = await EmitterHost.Run(resolve.Model!, configs, registry, outputRoot: null);
        run.Diagnostics.Should().BeEmpty();
        return run.EmitterBuffers["csharp"].Snapshot().Keys.ToList();
    }

    [Fact]
    public async Task EmitEventsFalse_SuppressesEventsFile_ButKeepsRecordAndState()
    {
        var files = await EmitFileNames(emitEvents: false);

        files.Should().NotContain(k => k.EndsWith("WorkerEvents.cs", System.StringComparison.Ordinal),
            because: "emit_events: false must suppress the events file");
        files.Should().Contain(k => k.EndsWith("Worker.cs", System.StringComparison.Ordinal),
            because: "the entity record is still emitted");
        files.Should().Contain(k => k.EndsWith("WorkerState.cs", System.StringComparison.Ordinal),
            because: "the State enum is still emitted");
    }

    [Fact]
    public async Task EmitEventsDefault_StillEmitsEventsFile()
    {
        var files = await EmitFileNames(emitEvents: null);

        files.Should().Contain(k => k.EndsWith("WorkerEvents.cs", System.StringComparison.Ordinal),
            because: "the events file is emitted by default (no regression)");
    }
}
