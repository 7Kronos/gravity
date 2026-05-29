using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// The public contract every Gravity emitter implements. Shipped through the
/// <c>Gravity.Dsl.Emitter</c> NuGet package; once published it cannot change
/// without an <c>AstVersion</c> major bump (constitution — Architectural
/// constraints, Stable AST contract).
/// </summary>
public interface IEmitter
{
    /// <summary>Stable identifier for this target (e.g. <c>"csharp"</c>). Used by the host as the diagnostics origin and by config to address this emitter's block.</summary>
    string TargetName { get; }

    /// <summary>
    /// Annotation namespace the emitter claims (FR-051/FR-052), e.g. <c>"csharp"</c>.
    /// Empty string means the emitter claims nothing — useful for diagnostic emitters
    /// or no-op stubs in tests. Two registered emitters may not claim the same
    /// non-empty namespace; the host emits <c>HOST002</c> when they do.
    /// </summary>
    string AnnotationNamespace { get; }

    /// <summary>
    /// AST contract range the emitter is built against. The host compares this against
    /// <see cref="Gravity.Dsl.Ast.AstVersion.Value"/> and rejects incompatible emitters
    /// with <c>HOST001</c>.
    /// </summary>
    SemanticVersionRange SupportedAstVersions { get; }

    /// <summary>Configuration schema the host uses to validate the user's <c>.gravity.yaml</c> block.</summary>
    EmitterConfigSchema ConfigurationSchema { get; }

    /// <summary>Produce output for <paramref name="model"/> into <paramref name="sink"/>.</summary>
    EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink);
}
