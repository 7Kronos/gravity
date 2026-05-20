using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// AC-5.2 — pins the <see cref="PostgresDdlEmitter"/> identity surface:
/// TargetName, AnnotationNamespace, SupportedAstVersions, ConfigurationSchema.
/// These are constitution-level invariants (LD-19, FR-401, FR-402); changing
/// them is a breaking change for any consumer.
/// </summary>
public sealed class RegistrationTests
{
    [Fact]
    public void TargetName_IsPostgresDdl()
    {
        new PostgresDdlEmitter().TargetName.Should().Be("postgres-ddl");
    }

    [Fact]
    public void AnnotationNamespace_IsPostgres()
    {
        // LD-19: identifiers cannot contain hyphens, so the annotation
        // namespace is "postgres" while the target name is "postgres-ddl".
        new PostgresDdlEmitter().AnnotationNamespace.Should().Be("postgres");
    }

    [Fact]
    public void SupportedAstVersions_AdmitsPhase03And8AstVersions()
    {
        var emitter = new PostgresDdlEmitter();
        emitter.SupportedAstVersions.Satisfies("1.0.0").Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("1.1.0").Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies(AstVersion.Value).Should().BeTrue();
        emitter.SupportedAstVersions.Satisfies("2.0.0").Should().BeFalse();
    }

    [Fact]
    public void Registry_Discovers_PostgresDdlEmitter_WithoutDiagnostics()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new PostgresDdlEmitter() });
        registry.Diagnostics.Should().BeEmpty();
        registry.Emitters.Should().ContainSingle().Which.TargetName.Should().Be("postgres-ddl");
        registry.ClaimedAnnotationNamespaces().Should().BeEquivalentTo(new[] { "postgres" });
    }

    [Fact]
    public void ConfigurationSchema_DeclaresThreeKeys()
    {
        var schema = new PostgresDdlEmitter().ConfigurationSchema;
        schema.Keys.Should().HaveCount(3);

        schema.Keys.Should().ContainSingle(k => k.Name == "output")
            .Which.Should().Match<ConfigKey>(k =>
                k.Name == "output" && k.Kind == ConfigValueKind.String && k.Required && k.Default == null);

        schema.Keys.Should().ContainSingle(k => k.Name == "schema")
            .Which.Should().Match<ConfigKey>(k =>
                k.Name == "schema" && k.Kind == ConfigValueKind.String && !k.Required
                && (string)k.Default! == "public");

        schema.Keys.Should().ContainSingle(k => k.Name == "migration_prefix")
            .Which.Should().Match<ConfigKey>(k =>
                k.Name == "migration_prefix" && k.Kind == ConfigValueKind.String && !k.Required
                && (string)k.Default! == "V");
    }

    [Fact]
    public void Host002_FiresWhenAnotherEmitterClaimsPostgresNamespace()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new PostgresDdlEmitter(),
            new StubPostgresClaimant(),
        });
        var host002 = registry.Diagnostics.Where(d => d.RuleId == "HOST002").ToArray();
        host002.Should().HaveCount(1);
        // Sorted ordinal: "postgres-ddl" < "stub-postgres".
        host002[0].Message.Should().Be(
            "annotation namespace 'postgres' is claimed by both 'postgres-ddl' and 'stub-postgres'");
    }

    [Fact]
    public void CoexistsWith_CSharpAndJsonSchema_Emitters()
    {
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new PostgresDdlEmitter(),
            new Gravity.Dsl.Emitter.JsonSchema.JsonSchemaEmitter(),
            new Gravity.Dsl.Emitter.CSharp.CSharpEmitter(),
        });
        registry.Diagnostics.Should().BeEmpty(
            because: "postgres, json_schema, and csharp are disjoint annotation namespaces");
        registry.Emitters.Should().HaveCount(3);
    }

    private sealed class StubPostgresClaimant : IEmitter
    {
        public string TargetName => "stub-postgres";
        public string AnnotationNamespace => "postgres";
        public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
        public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;
        public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
            => new(ImmutableArray<Diagnostic>.Empty);
    }
}
