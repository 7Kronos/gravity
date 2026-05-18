using System.Collections.Immutable;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;

namespace Gravity.Dsl.Tests.Stubs;

/// <summary>
/// Stub emitter pair that both claim the annotation namespace <c>csharp</c>.
/// Used by <c>AnnotationNamespaceOwnershipTests</c> to assert <c>HOST002</c>.
/// </summary>
public sealed class CollidingCsharpEmitterA : IEmitter
{
    public string TargetName => "alpha-csharp";
    public string AnnotationNamespace => "csharp";
    public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
    public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
        => new(ImmutableArray<Diagnostic>.Empty);
}

/// <summary>Second stub in the colliding pair (target name sorts after the first).</summary>
public sealed class CollidingCsharpEmitterB : IEmitter
{
    public string TargetName => "beta-csharp";
    public string AnnotationNamespace => "csharp";
    public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
    public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
        => new(ImmutableArray<Diagnostic>.Empty);
}
