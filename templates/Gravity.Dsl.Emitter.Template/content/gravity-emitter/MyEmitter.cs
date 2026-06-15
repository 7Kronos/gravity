using System;
using System.Collections.Immutable;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;

namespace MyEmitter;

/// <summary>
/// Custom Gravity DSL emitter scaffolded by <c>dotnet new gravity-emitter</c>.
/// Replace the body of <see cref="Emit"/> with your target-specific rendering.
/// </summary>
/// <remarks>
/// The shape of this class is the entire contract — see
/// <c>docs/emitter-authoring-guide.md</c> in the Gravity repo for the full
/// walkthrough.
///
/// <list type="bullet">
///   <item><see cref="TargetName"/> — stable identifier used by <c>.gravity.yaml</c>.</item>
///   <item><see cref="AnnotationNamespace"/> — annotation namespace this emitter claims.</item>
///   <item><see cref="SupportedAstVersions"/> — AST contract range you compiled against.</item>
///   <item><see cref="ConfigurationSchema"/> — schema the host validates the user's YAML against.</item>
///   <item><see cref="Emit"/> — the only entry point, called once per build.</item>
/// </list>
/// </remarks>
public sealed class MyEmitter : IEmitter
{
    /// <summary>Configuration key naming the relative output directory under the host's root.</summary>
    public const string ConfigKeyOutput = "output";

    /// <inheritdoc/>
    public string TargetName => "target-name-placeholder";

    /// <inheritdoc/>
    public string AnnotationNamespace => "annotation-ns-placeholder";

    /// <inheritdoc/>
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse("ast-version-range-placeholder");

    /// <inheritdoc/>
    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput, ConfigValueKind.String, Required: true, Default: null)
    ));

    /// <inheritdoc/>
    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        var output = config.GetString(ConfigKeyOutput);

        // Walk Declarations in (FQN ordinal, Version asc) order — the contract
        // pinned by ResolvedModel.Declarations being an ImmutableSortedDictionary.
        // The samples/emitters/outline sample in the Gravity repo is the canonical
        // shape; copy its BuildDeclToFile + ComposeDirectory helpers when you need
        // namespace-aware directory layout.
        foreach (var kv in model.Declarations)
        {
            switch (kv.Value)
            {
                case EntityDecl entity:
                    sink.WriteFile(
                        output + "/" + entity.Name + ".txt",
                        entity.Name + " v" + kv.Key.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                    break;
                case ValueTypeDecl _:
                case EnumDecl _:
                    // Extend with your own rendering for value types and enums.
                    break;
            }
        }

        return new EmitResult(ImmutableArray<Diagnostic>.Empty);
    }
}
