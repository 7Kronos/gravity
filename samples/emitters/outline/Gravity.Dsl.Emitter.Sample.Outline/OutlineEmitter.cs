using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter.Sample.Outline.Render;

namespace Gravity.Dsl.Emitter.Sample.Outline;

/// <summary>
/// Sample emitter — emits one Markdown outline file per <see cref="EntityDecl"/>,
/// plus minimal Markdown for <see cref="ValueTypeDecl"/> and <see cref="EnumDecl"/>.
/// Intentionally tiny: this class exists as a copy-paste template for community
/// emitter authors (LD-10 / LD-12), not as a production target. The shape mirrors
/// <c>Gravity.Dsl.Emitter.CSharp.CSharpEmitter</c> so prospective authors find the
/// idioms (sealed class, <see cref="SemanticVersionRange"/>, <see cref="EmitterConfigSchema"/>
/// declared via <see cref="ImmutableArray.Create{T}(T)"/>, ordinal iteration order)
/// in the same place they remember them from the reference emitter.
/// See specs/003-phase-9-build-integration/spec.md FR-220..FR-225.
/// </summary>
public sealed class OutlineEmitter : IEmitter
{
    /// <summary>Configuration key naming the relative output directory under the host's root.</summary>
    public const string ConfigKeyOutput = "output";

    /// <inheritdoc/>
    public string TargetName => "outline";

    /// <inheritdoc/>
    public string AnnotationNamespace => "outline";

    /// <inheritdoc/>
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");

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

        // Project the validated config into the typed view. The `output:` key names
        // the relative root under the emitter host's outputRoot — every file written
        // by this emitter is prefixed with it (FR-220..FR-225).
        var typedConfig = OutlineEmitterConfig.From(config);

        // Map declaration FQN -> SourceFile so we can resolve each declaration's
        // DSL namespace for directory layout. Identical helper shape to the C#
        // reference emitter's BuildDeclToFile (CSharpEmitter.cs line 105) so a
        // community author copy-pasting this template recognises the idiom.
        var declToFile = BuildDeclToFile(model);

        // Walk Declarations in (FQN ordinal, Version asc) order — the FR-161 contract
        // pinned by ResolvedModel.Declarations being an ImmutableSortedDictionary.
        foreach (var kv in model.Declarations)
        {
            var decl = kv.Value;
            if (!declToFile.TryGetValue(kv.Key.Fqn, out var sourceFile))
            {
                continue;
            }
            string? dslNs = sourceFile.Namespace?.Name;
            string dir = Combine(typedConfig.Output, ComposeDirectory(dslNs));

            switch (decl)
            {
                case EntityDecl entity:
                    sink.WriteFile(
                        Combine(dir, entity.Name + ".md"),
                        EntityOutlineRenderer.Render(entity, kv.Key.Version));
                    break;
                case ValueTypeDecl vt:
                    sink.WriteFile(
                        Combine(dir, vt.Name + ".md"),
                        ValueTypeOutlineRenderer.Render(vt, kv.Key.Version));
                    break;
                case EnumDecl en:
                    sink.WriteFile(
                        Combine(dir, en.Name + ".md"),
                        EnumOutlineRenderer.Render(en, kv.Key.Version));
                    break;
            }
        }

        return new EmitResult(ImmutableArray<Diagnostic>.Empty);
    }

    private static Dictionary<string, SourceFile> BuildDeclToFile(ResolvedModel model)
    {
        var map = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var file in model.Files)
        {
            string ns = file.Namespace?.Name ?? string.Empty;
            foreach (var decl in file.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                if (!map.ContainsKey(fqn))
                {
                    map[fqn] = file;
                }
            }
        }
        return map;
    }

    private static string ComposeDirectory(string? dslNamespace)
    {
        // Phase 9 sample mirrors the C# reference emitter's directory layout
        // (NamespaceMapper.ComposeDirectory): one directory per dotted-namespace segment.
        if (string.IsNullOrEmpty(dslNamespace)) return string.Empty;
        return dslNamespace.Replace('.', '/');
    }

    private static string Combine(string dir, string name)
    {
        if (string.IsNullOrEmpty(dir)) return name;
        if (string.IsNullOrEmpty(name)) return dir.Replace('\\', '/');
        // BufferedEmitterOutput normalises to '/' anyway, but we never let a backslash
        // appear in the buffer key so determinism stays clean on Windows.
        return (dir + "/" + name).Replace('\\', '/');
    }
}
