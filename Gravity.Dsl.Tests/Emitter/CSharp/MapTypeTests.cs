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
/// Map (dictionary) → C# rendering. A <c>Map&lt;K, V&gt;</c> property renders as
/// <c>ImmutableDictionary&lt;K, V&gt;</c>, pulls in
/// <c>using System.Collections.Immutable;</c>, and an optional map becomes a
/// nullable reference.
/// </summary>
public sealed class MapTypeTests
{
    private static async Task<string> RenderEntity(string propertyDecl)
    {
        var src =
$@"namespace hr;
entity F version 1 {{
  identity id: UUID;
  properties {{
    {propertyDecl};
  }}
  lifecycle {{
    states {{ Active; }}
    transitions {{}}
  }}
  events {{}}
  commands {{}}
}}";
        var parsed = Parser.Parse("Fixture.gravity", src);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull();

        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["csharp"] = new EmitterConfig(
                TargetName: "csharp", Enabled: true, Output: "gen/csharp",
                Values: ImmutableSortedDictionary<string, object>.Empty
                    .Add("output", "gen/csharp")
                    .Add("namespace", "AcmeCo.Domain")
                    .Add("file_scoped_namespaces", true)),
        };
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new CSharpEmitter() });
        var run = await EmitterHost.Run(resolve.Model!, configs, registry, outputRoot: null);
        run.Diagnostics.Should().BeEmpty();
        var buffers = run.EmitterBuffers["csharp"].Snapshot();
        return buffers.Single(kv => kv.Key.EndsWith("F.cs", System.StringComparison.Ordinal)).Value;
    }

    [Fact]
    public async Task RequiredMap_RendersImmutableDictionary_AndUsing()
    {
        var cs = await RenderEntity("external_ids: Map<String, String>");
        cs.Should().Contain("ImmutableDictionary<string, string> ExternalIds");
        cs.Should().Contain("using System.Collections.Immutable;");
    }

    [Fact]
    public async Task OptionalMap_RendersNullable()
    {
        var cs = await RenderEntity("external_ids: Map<String, String>?");
        cs.Should().Contain("ImmutableDictionary<string, string>? ExternalIds");
    }

    [Fact]
    public async Task MapValue_IsRenderedRecursively()
    {
        // Map<String, Int> -> ImmutableDictionary<string, int>.
        var cs = await RenderEntity("counts: Map<String, Int>");
        cs.Should().Contain("ImmutableDictionary<string, int> Counts");
    }
}
