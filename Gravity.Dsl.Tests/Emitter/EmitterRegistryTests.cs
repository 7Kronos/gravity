using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Tests.Stubs;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter;

public sealed class EmitterRegistryTests
{
    [Fact]
    public void Discover_IncompatibleAstVersion_RejectsWithHost001()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new IncompatibleAstEmitter(),
            new NoopEmitter()
        });

        registry.Emitters.Should().ContainSingle()
            .Which.TargetName.Should().Be("noop");
        registry.Diagnostics.Should().ContainSingle(d => d.RuleId == "HOST001")
            .Which.Message.Should().Contain("future").And.Contain(">=2.0.0");
    }

    [Fact]
    public void Discover_ReturnsImmutableSortedByTargetName()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new NoopEmitter(),
            new CollidingCsharpEmitterB(),
            new CollidingCsharpEmitterA()
        });

        var names = registry.Emitters.Select(e => e.TargetName).ToArray();
        names.Should().Equal("alpha-csharp", "beta-csharp", "noop");
    }

    [Fact]
    public void ClaimedAnnotationNamespaces_SkipsEmpty()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new NoopEmitter(),
            new CollidingCsharpEmitterA()
        });

        var claimed = registry.ClaimedAnnotationNamespaces();
        claimed.Should().BeEquivalentTo(new[] { "csharp" });
    }
}
