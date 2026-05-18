using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter.CSharp;

/// <summary>
/// Reference C# emitter for the Gravity DSL. Produces idiomatic C# 12 record
/// declarations for value types, enums, entities (record + state enum + events
/// file + commands file). Output is deterministic: declarations are walked in
/// FQN-ordinal order, files are written through <see cref="IEmitterOutput.WriteFile"/>
/// and ultimately serialised in path-sorted order by <see cref="BufferedEmitterOutput"/>.
/// </summary>
public sealed class CSharpEmitter : IEmitter
{
    /// <summary>Configuration key for the optional namespace prefix.</summary>
    public const string ConfigKeyNamespace = "namespace";

    /// <summary>Configuration key for the file-scoped-namespaces toggle.</summary>
    public const string ConfigKeyFileScopedNamespaces = "file_scoped_namespaces";

    /// <inheritdoc/>
    public string TargetName => "csharp";

    /// <inheritdoc/>
    public string AnnotationNamespace => "csharp";

    /// <inheritdoc/>
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");

    /// <inheritdoc/>
    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyNamespace, ConfigValueKind.String, Required: false, Default: null),
        new ConfigKey(ConfigKeyFileScopedNamespaces, ConfigValueKind.Bool, Required: false, Default: true)
    ));

    /// <inheritdoc/>
    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        string? configPrefix = TryGetString(config, ConfigKeyNamespace);
        bool fileScoped = TryGetBool(config, ConfigKeyFileScopedNamespaces, defaultValue: true);

        // Map declaration name -> source SourceFile so we can resolve the DSL
        // namespace and the original .gravity relative path for the header.
        var declToFile = BuildDeclToFile(model);

        // Walk Declarations in (FQN ordinal, Version asc) order (FR-161, enforced by
        // ImmutableSortedDictionary<DeclKey, TopLevelDecl> with DeclKeyComparer).
        foreach (var kv in model.Declarations)
        {
            var decl = kv.Value;
            var sourceFile = declToFile[kv.Key.Fqn];
            string? dslNs = sourceFile.Namespace?.Name;
            string csharpNs = NamespaceMapper.Compose(dslNs, configPrefix);
            string dir = NamespaceMapper.ComposeDirectory(dslNs);
            string sourceRel = GravityRelativePath(sourceFile.Path, model);

            switch (decl)
            {
                case ValueTypeDecl vt:
                    EmitOne(sink, Path.Combine(dir, vt.Name + ".cs"), sourceRel,
                        Renderers.RenderValueType(vt, csharpNs, fileScoped));
                    break;
                case EnumDecl en:
                    EmitOne(sink, Path.Combine(dir, en.Name + ".cs"), sourceRel,
                        Renderers.RenderEnum(en, csharpNs, fileScoped));
                    break;
                case EntityDecl entity:
                    EmitOne(sink, Path.Combine(dir, entity.Name + ".cs"), sourceRel,
                        Renderers.RenderEntityRecord(entity, csharpNs, fileScoped));
                    EmitOne(sink, Path.Combine(dir, entity.Name + "State.cs"), sourceRel,
                        Renderers.RenderStateEnum(entity, csharpNs, fileScoped));
                    if (entity.Events.Length > 0)
                    {
                        EmitOne(sink, Path.Combine(dir, entity.Name + "Events.cs"), sourceRel,
                            Renderers.RenderEvents(entity, csharpNs, fileScoped));
                    }
                    if (entity.Commands.Length > 0)
                    {
                        EmitOne(sink, Path.Combine(dir, entity.Name + "Commands.cs"), sourceRel,
                            Renderers.RenderCommands(entity, csharpNs, fileScoped));
                    }
                    break;
            }
        }

        return new EmitResult(ImmutableArray<Diagnostic>.Empty);
    }

    private static void EmitOne(IEmitterOutput sink, string relPath, string sourceGravityRelPath, string body)
    {
        var formatted = CSharpFileFormatter.Format(body, sourceGravityRelPath);
        // Normalise path separators for cross-platform stability inside the buffer.
        sink.WriteFile(relPath.Replace('\\', '/'), formatted);
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
                // First-seen wins; duplicates would already be caught by the resolver.
                if (!map.ContainsKey(fqn))
                {
                    map[fqn] = file;
                }
            }
        }
        return map;
    }

    /// <summary>
    /// Compute a stable relative path for the header. Uses the file's name without
    /// any leading directory tree so the header survives in golden files regardless
    /// of where the input root was rooted at run time.
    /// </summary>
    private static string GravityRelativePath(string fullPath, ResolvedModel model)
    {
        _ = model;
        try
        {
            return Path.GetFileName(fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private static string? TryGetString(EmitterConfig config, string key)
    {
        if (!config.Values.TryGetValue(key, out var raw)) return null;
        return raw as string;
    }

    private static bool TryGetBool(EmitterConfig config, string key, bool defaultValue)
    {
        if (!config.Values.TryGetValue(key, out var raw)) return defaultValue;
        return raw is bool b ? b : defaultValue;
    }
}
