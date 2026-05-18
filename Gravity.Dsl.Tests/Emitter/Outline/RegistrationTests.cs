using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.Sample.Outline;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.Outline;

/// <summary>
/// Pins the <see cref="OutlineEmitter"/> identity surface — TargetName,
/// AnnotationNamespace, supported AST range, and configuration schema (FR-220 / FR-223).
/// These are constitution-level invariants and changing them is a breaking change
/// for any consumer (LD-12).
/// </summary>
public sealed class OutlineEmitterRegistrationTests
{
    [Fact]
    public void TargetName_IsOutline()
    {
        new OutlineEmitter().TargetName.Should().Be("outline");
    }

    [Fact]
    public void AnnotationNamespace_IsOutline()
    {
        new OutlineEmitter().AnnotationNamespace.Should().Be("outline");
    }

    [Fact]
    public void SupportedAstVersions_AdmitsCurrentAst()
    {
        var emitter = new OutlineEmitter();
        // Phase 8 bumped the AST to 1.1.0; the range >=1.0.0 <2.0.0 must continue
        // to admit the current AstVersion so the host does not reject the emitter
        // with HOST001 on registration.
        emitter.SupportedAstVersions.Satisfies(AstVersion.Value).Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("1.0.0").Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("1.999.0").Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("2.0.0").Should().BeFalse();
    }

    [Fact]
    public void Registry_DiscoversOutlineEmitter_WithoutDiagnostics()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new OutlineEmitter() });
        registry.Diagnostics.Should().BeEmpty();
        registry.Emitters.Should().ContainSingle().Which.TargetName.Should().Be("outline");
        registry.ClaimedAnnotationNamespaces().Should().BeEquivalentTo(new[] { "outline" });
    }

    [Fact]
    public void ConfigurationSchema_DeclaresOnlyRequiredOutputKey()
    {
        var schema = new OutlineEmitter().ConfigurationSchema;
        schema.Keys.Should().ContainSingle()
            .Which.Should().Match<ConfigKey>(k =>
                k.Name == "output"
                && k.Kind == ConfigValueKind.String
                && k.Required
                && k.Default == null);
    }
}
