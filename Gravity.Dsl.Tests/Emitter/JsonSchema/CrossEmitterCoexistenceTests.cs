using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.CSharp;
using Gravity.Dsl.Emitter.JsonSchema;
using Gravity.Dsl.Emitter.Sample.Outline;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.10 — cross-emitter coexistence. Enrol the JSON Schema emitter
/// alongside the C# reference emitter and the outline sample emitter; assert
/// the three claimed annotation namespaces (<c>csharp</c>, <c>outline</c>,
/// <c>json_schema</c>) are disjoint and HOST002 does not fire.
/// </summary>
public sealed class CrossEmitterCoexistenceTests
{
    [Fact]
    public void ThreeEmitters_RegisterTogether_WithoutHost002()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new CSharpEmitter(),
            new OutlineEmitter(),
            new JsonSchemaEmitter(),
        });
        registry.Diagnostics.Should().NotContain(d => d.RuleId == "HOST002");
        registry.Diagnostics.Should().NotContain(d => d.RuleId == "HOST001");
        // Sorted by TargetName ordinal: csharp, json-schema, outline.
        registry.Emitters.Select(e => e.TargetName).Should().Equal(
            new[] { "csharp", "json-schema", "outline" });
        registry.ClaimedAnnotationNamespaces().Should().BeEquivalentTo(
            new[] { "csharp", "outline", "json_schema" });
    }
}
