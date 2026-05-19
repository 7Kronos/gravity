using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;
using Gravity.Dsl.Emitter.JsonSchema.Render;

namespace Gravity.Dsl.Emitter.JsonSchema;

/// <summary>
/// JSON Schema (Draft-07) reference emitter. Produces per-entity bundle files
/// (entity-state root schema + per-event payload + per-command request/response
/// in <c>definitions</c>), per-value-type files, and per-enum files. Output is
/// byte-deterministic Draft-07; configuration takes <c>output</c> (required)
/// and <c>bundle_strategy</c> (optional, default <c>"per-entity"</c> — the only
/// legal value in v1).
/// </summary>
public sealed class JsonSchemaEmitter : IEmitter
{
    /// <summary>Configuration key naming the relative output directory.</summary>
    public const string ConfigKeyOutput = "output";

    /// <summary>Configuration key naming the bundle layout strategy.</summary>
    public const string ConfigKeyBundleStrategy = "bundle_strategy";

    /// <summary>Only legal value for <c>bundle_strategy</c> in v1.</summary>
    public const string DefaultBundleStrategy = "per-entity";

    /// <summary>The Draft-07 metaschema URI written into every emitted schema.</summary>
    internal const string Draft07Uri = "http://json-schema.org/draft-07/schema#";

    /// <summary>Constant TargetName value; exposed for use by config diagnostic spans.</summary>
    internal const string TargetNameValue = "json-schema";

    /// <inheritdoc/>
    public string TargetName => TargetNameValue;

    /// <inheritdoc/>
    public string AnnotationNamespace => "json_schema";

    /// <inheritdoc/>
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");

    /// <inheritdoc/>
    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput, ConfigValueKind.String, Required: true, Default: null),
        new ConfigKey(ConfigKeyBundleStrategy, ConfigValueKind.String, Required: false, Default: DefaultBundleStrategy)
    ));

    /// <inheritdoc/>
    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        // 1. Project + pre-flight the validated config (FR-302 → JS002).
        var typed = JsonSchemaEmitterConfig.From(config, diags);
        if (typed is null)
        {
            return new EmitResult(diags.ToImmutable());
        }

        // 2. Detect multi-version FQN coexistence (FR-310 / FR-318).
        var multiVersionFqns = ComputeMultiVersionFqns(model);

        // 3. Map FQN -> SourceFile so we resolve each decl's DSL namespace.
        var declToFile = BuildDeclToFile(model);

        // 4. Walk Declarations in (FQN ordinal, Version asc) order — the FR-161
        //    contract pinned by ResolvedModel.Declarations being an
        //    ImmutableSortedDictionary.
        foreach (var kv in model.Declarations)
        {
            if (!declToFile.TryGetValue(kv.Key.Fqn, out var sourceFile))
            {
                continue;
            }
            string? dslNs = sourceFile.Namespace?.Name;
            string dir = Combine(typed.Output, ComposeDirectory(dslNs));
            int version = kv.Key.Version;
            bool versioned = multiVersionFqns.Contains(kv.Key.Fqn);

            switch (kv.Value)
            {
                case EntityDecl entity:
                    {
                        var body = EntityBundleRenderer.Render(
                            entity, model, kv.Key, dslNs, multiVersionFqns, diags);
                        sink.WriteFile(BundleFilename(dir, entity.Name, version, versioned), body);
                        break;
                    }
                case ValueTypeDecl vt:
                    {
                        var body = ValueTypeRenderer.Render(
                            vt, model, kv.Key, dslNs, multiVersionFqns, diags);
                        sink.WriteFile(BundleFilename(dir, vt.Name, version, versioned), body);
                        break;
                    }
                case EnumDecl en:
                    {
                        var body = EnumRenderer.Render(en, kv.Key);
                        sink.WriteFile(BundleFilename(dir, en.Name, version, versioned), body);
                        break;
                    }
            }
        }

        return new EmitResult(diags.ToImmutable());
    }

    /// <summary>
    /// FR-310 filename rule. Multi-version case (FQN has &gt;1 version in scope)
    /// → <c>&lt;name&gt;.v&lt;N&gt;.json</c> for every version (symmetric
    /// layout, no off-by-one). Single-version case → <c>&lt;name&gt;.json</c>.
    /// </summary>
    private static string BundleFilename(string dir, string name, int version, bool versioned)
    {
        string file = versioned
            ? name + ".v" + version.ToString(CultureInfo.InvariantCulture) + ".json"
            : name + ".json";
        return Combine(dir, file);
    }

    /// <summary>
    /// One pass over <c>model.Declarations</c> producing the set of FQNs that
    /// have more than one Version in scope (FR-310 / FR-318 multi-version
    /// trigger).
    /// </summary>
    private static HashSet<string> ComputeMultiVersionFqns(ResolvedModel model)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in model.Declarations)
        {
            if (counts.TryGetValue(kv.Key.Fqn, out var c))
            {
                counts[kv.Key.Fqn] = c + 1;
            }
            else
            {
                counts[kv.Key.Fqn] = 1;
            }
        }
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in counts)
        {
            if (kv.Value > 1)
            {
                result.Add(kv.Key);
            }
        }
        return result;
    }

    /// <summary>
    /// Map declaration FQN → SourceFile. Identical helper shape to
    /// <c>CSharpEmitter.BuildDeclToFile</c>; duplicated intentionally (the JSON
    /// Schema emitter must not link <c>Gravity.Dsl.Emitter.CSharp</c> per
    /// FR-300 / FR-363).
    /// </summary>
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

    /// <summary>
    /// Compose the relative file path for a declaration in
    /// <paramref name="dslNamespace"/>. Mirrors
    /// <c>CSharpEmitter.NamespaceMapper.ComposeDirectory</c>. Each dotted
    /// segment is defense-in-depth checked against path-traversal sequences
    /// (the grammar's identifier rule already forbids them).
    /// </summary>
    private static string ComposeDirectory(string? dslNamespace)
    {
        if (string.IsNullOrEmpty(dslNamespace)) return string.Empty;
        foreach (var segment in dslNamespace.Split('.'))
        {
            ValidateNamespaceSegment(segment);
        }
        return dslNamespace.Replace('.', '/');
    }

    /// <summary>
    /// Defense-in-depth guard against namespace segments that would produce
    /// path-traversal or path-injection artifacts (<c>..</c>, embedded
    /// <c>/</c>, embedded <c>\</c>, empty). The grammar identifier rule
    /// <c>[A-Za-z][A-Za-z0-9_]*</c> already rejects these at parse time;
    /// this guard catches synthetic <see cref="ResolvedModel"/> inputs that
    /// bypass the parser.
    /// </summary>
    internal static void ValidateNamespaceSegment(string segment)
    {
        if (segment.Length == 0 || segment == ".." || segment.Contains('/') || segment.Contains('\\'))
        {
            throw new InvalidOperationException(
                "Namespace segment '" + segment + "' is not a safe path component.");
        }
    }

    private static string Combine(string dir, string name)
    {
        if (string.IsNullOrEmpty(dir)) return name;
        if (string.IsNullOrEmpty(name)) return dir.Replace('\\', '/');
        return (dir + "/" + name).Replace('\\', '/');
    }
}
