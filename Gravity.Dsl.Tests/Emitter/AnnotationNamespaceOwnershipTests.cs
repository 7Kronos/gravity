using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Tests.Stubs;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter;

public sealed class AnnotationNamespaceOwnershipTests
{
    [Fact]
    public void TwoEmitters_SameNamespace_ProducesHost002WithSortedTargetNames()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            // Insert in reverse target-name order to prove sorting happens on output.
            new CollidingCsharpEmitterB(),
            new CollidingCsharpEmitterA()
        });

        var host002 = registry.Diagnostics.Where(d => d.RuleId == "HOST002").ToArray();
        host002.Should().HaveCount(1);
        host002[0].Message.Should().Be(
            "annotation namespace 'csharp' is claimed by both 'alpha-csharp' and 'beta-csharp'");
    }

    [Fact]
    public void SingleEmitter_NoCollision_NoHost002()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new CollidingCsharpEmitterA()
        });

        registry.Diagnostics.Should().NotContain(d => d.RuleId == "HOST002");
    }
}
