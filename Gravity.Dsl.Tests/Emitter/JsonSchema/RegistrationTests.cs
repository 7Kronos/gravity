using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.JsonSchema;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.2 — pins the <see cref="JsonSchemaEmitter"/> identity surface:
/// TargetName, AnnotationNamespace, SupportedAstVersions, ConfigurationSchema.
/// These are constitution-level invariants (LD-17, FR-301, FR-302); changing
/// them is a breaking change for any consumer.
/// </summary>
public sealed class RegistrationTests
{
    [Fact]
    public void TargetName_IsJsonSchema()
    {
        new JsonSchemaEmitter().TargetName.Should().Be("json-schema");
    }

    [Fact]
    public void AnnotationNamespace_IsJsonSchema_Underscore()
    {
        // LD-17: identifiers cannot contain hyphens, so the annotation
        // namespace uses underscore even though the target name uses kebab.
        new JsonSchemaEmitter().AnnotationNamespace.Should().Be("json_schema");
    }

    [Fact]
    public void SupportedAstVersions_AdmitsPhase03And8AstVersions()
    {
        var emitter = new JsonSchemaEmitter();
        // FR-301: spans both Phase 0–3 1.0.0 and Phase 8 1.1.0 so the emitter
        // compiles against either without a rebuild.
        emitter.SupportedAstVersions.Satisfies("1.0.0").Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("1.1.0").Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies(AstVersion.Value).Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("2.0.0").Should().BeFalse();
    }

    [Fact]
    public void Registry_Discovers_JsonSchemaEmitter_WithoutDiagnostics()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new JsonSchemaEmitter() });
        registry.Diagnostics.Should().BeEmpty();
        registry.Emitters.Should().ContainSingle().Which.TargetName.Should().Be("json-schema");
        registry.ClaimedAnnotationNamespaces().Should().BeEquivalentTo(new[] { "json_schema" });
    }

    [Fact]
    public void ConfigurationSchema_DeclaresOutputAndBundleStrategy()
    {
        var schema = new JsonSchemaEmitter().ConfigurationSchema;
        schema.Keys.Should().HaveCount(2);
        schema.Keys.Should().ContainSingle(k => k.Name == "output")
            .Which.Should().Match<ConfigKey>(k =>
                k.Name == "output"
                && k.Kind == ConfigValueKind.String
                && k.Required
                && k.Default == null);
        schema.Keys.Should().ContainSingle(k => k.Name == "bundle_strategy")
            .Which.Should().Match<ConfigKey>(k =>
                k.Name == "bundle_strategy"
                && k.Kind == ConfigValueKind.String
                && !k.Required
                && (string)k.Default! == "per-entity");
    }

    [Fact]
    public void Host002_FiresWhenAnotherEmitterClaimsJsonSchemaNamespace()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new JsonSchemaEmitter(),
            new StubJsonSchemaClaimant(),
        });
        var host002 = registry.Diagnostics.Where(d => d.RuleId == "HOST002").ToArray();
        host002.Should().HaveCount(1);
        // Sorted ordinal: "json-schema" < "stub-json-schema".
        host002[0].Message.Should().Be(
            "annotation namespace 'json_schema' is claimed by both 'json-schema' and 'stub-json-schema'");
    }

    /// <summary>Stub claimant that uses an alphabetically-later TargetName but the same namespace.</summary>
    private sealed class StubJsonSchemaClaimant : IEmitter
    {
        public string TargetName => "stub-json-schema";
        public string AnnotationNamespace => "json_schema";
        public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
        public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;
        public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
            => new(ImmutableArray<Diagnostic>.Empty);
    }
}
