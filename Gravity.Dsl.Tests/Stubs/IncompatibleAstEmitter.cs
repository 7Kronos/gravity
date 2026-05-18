using System.Collections.Immutable;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;

namespace Gravity.Dsl.Tests.Stubs;

/// <summary>
/// Stub emitter whose declared <c>SupportedAstVersions</c> excludes the current
/// <see cref="AstVersion.Value"/>. Used to assert <c>HOST001</c>.
/// </summary>
public sealed class IncompatibleAstEmitter : IEmitter
{
    public string TargetName => "future";
    public string AnnotationNamespace => string.Empty;
    public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=2.0.0");
    public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
        => new(ImmutableArray<Diagnostic>.Empty);
}
