using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;
using Gravity.Dsl.Emitter.PostgresDdl.Render;

namespace Gravity.Dsl.Emitter.PostgresDdl;

/// <summary>
/// PostgreSQL DDL reference emitter. Produces (a) idempotent baseline DDL
/// under <c>schema/</c> — one file per entity table (carrying the
/// <c>CREATE TABLE IF NOT EXISTS</c>, its lifecycle-state enum, foreign-key
/// constraints, and btree/GIN indexes), one file per namespace-scope value
/// type (composite <c>CREATE TYPE</c>), one file per namespace-scope enum;
/// and (b) a per-entity-version migration ledger under <c>migrations/</c>
/// where <c>V1__&lt;Entity&gt;.sql</c> is the baseline and
/// <c>V&lt;N&gt;__&lt;Entity&gt;.sql</c> are forward-only additive diffs.
/// Target PostgreSQL schema is configurable via the <c>schema</c> key
/// (default <c>"public"</c>); migration filename prefix is configurable via
/// <c>migration_prefix</c> (default <c>"V"</c>, Flyway-compatible).
/// Output is byte-deterministic. See
/// <c>specs/006-phase-5-postgres-ddl-emitter/spec.md</c>.
/// </summary>
public sealed class PostgresDdlEmitter : IEmitter
{
    /// <summary>Configuration key naming the relative output directory.</summary>
    public const string ConfigKeyOutput = "output";

    /// <summary>Configuration key naming the target PostgreSQL schema.</summary>
    public const string ConfigKeySchema = "schema";

    /// <summary>Configuration key naming the migration-filename prefix.</summary>
    public const string ConfigKeyMigrationPrefix = "migration_prefix";

    /// <summary>Default target PostgreSQL schema when <c>schema</c> is unset.</summary>
    public const string DefaultSchema = "public";

    /// <summary>Default migration-filename prefix; matches the Flyway convention.</summary>
    public const string DefaultMigrationPrefix = "V";

    /// <summary>Constant <see cref="TargetName"/> value; exposed for use by config diagnostic spans.</summary>
    internal const string TargetNameValue = "postgres-ddl";

    /// <inheritdoc/>
    public string TargetName => TargetNameValue;

    /// <inheritdoc/>
    public string AnnotationNamespace => "postgres";

    /// <inheritdoc/>
    public SemanticVersionRange SupportedAstVersions { get; } =
        SemanticVersionRange.Parse(">=1.0.0 <2.0.0");

    /// <inheritdoc/>
    public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
        new ConfigKey(ConfigKeyOutput,          ConfigValueKind.String, Required: true,  Default: null),
        new ConfigKey(ConfigKeySchema,          ConfigValueKind.String, Required: false, Default: DefaultSchema),
        new ConfigKey(ConfigKeyMigrationPrefix, ConfigValueKind.String, Required: false, Default: DefaultMigrationPrefix)
    ));

    /// <inheritdoc/>
    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        // 1. Project + pre-flight the validated config (FR-402 / FR-470 → PG001).
        var typed = PostgresDdlEmitterConfig.From(config, diags);
        if (typed is null)
        {
            return new EmitResult(diags.ToImmutable());
        }

        // 2. Detect multi-version FQN coexistence (FR-425 / FR-435).
        var multiVersionFqns = ComputeMultiVersionFqns(model);

        // 3. Map declaration FQN → SourceFile.
        var declToFile = BuildDeclToFile(model);

        // 4. Group declarations by FQN, preserving the (FQN ordinal, Version asc)
        //    iteration order pinned by ResolvedModel.Declarations being an
        //    ImmutableSortedDictionary (FR-403, FR-161).
        var groups = GroupByFqn(model);

        foreach (var group in groups)
        {
            string fqn = group.Key;
            var versions = group.Value;
            if (!declToFile.TryGetValue(fqn, out var sourceFile))
            {
                continue;
            }
            string? dslNs = sourceFile.Namespace?.Name;
            bool multiVersion = multiVersionFqns.Contains(fqn);

            // 4a. schema/ tree — one file per version (single-version: unqualified
            //     filename; multi-version: .v<N>.sql for every version, symmetric
            //     to FR-310's JSON Schema rule).
            foreach (var (key, decl) in versions)
            {
                string? body = decl switch
                {
                    EntityDecl entity      => EntityTableRenderer.Render(entity, model, key, typed, multiVersion, declToFile, multiVersionFqns, diags),
                    ValueTypeDecl valueT   => ValueTypeRenderer.Render(valueT, model, key, typed, multiVersion, multiVersionFqns),
                    EnumDecl enumDecl      => EnumRenderer.Render(enumDecl, key, typed, multiVersion),
                    _ => null,
                };
                if (body is not null)
                {
                    sink.WriteFile(SchemaPath(typed.Output, dslNs, decl.Name, key.Version, multiVersion), body);
                }
            }

            // 4b. migrations/ tree — V1 baseline + V<N> diffs (FR-426 / FR-427).
            MigrationRenderer.RenderAll(versions, model, typed, dslNs, declToFile, multiVersionFqns, sink, diags);
        }

        // The V1 migration baseline re-runs EntityTableRenderer.Render against
        // the same EntityDecl as the schema/ file, so per-property diagnostics
        // (PG002, PG003, PG004) get emitted twice. Dedupe by (RuleId, Message,
        // Span) — diagnostics are value-equal records so we keep the first
        // occurrence and drop subsequent duplicates while preserving order.
        var deduped = ImmutableArray.CreateBuilder<Diagnostic>();
        var seen = new HashSet<Diagnostic>();
        foreach (var d in diags)
        {
            if (seen.Add(d)) deduped.Add(d);
        }
        return new EmitResult(deduped.ToImmutable());
    }

    /// <summary>
    /// FR-425 filename rule. Multi-version case (FQN has &gt;1 version in scope)
    /// → <c>schema/&lt;ns&gt;/&lt;name&gt;.v&lt;N&gt;.sql</c> for every version.
    /// Single-version case → <c>schema/&lt;ns&gt;/&lt;name&gt;.sql</c>.
    /// </summary>
    private static string SchemaPath(string output, string? dslNamespace, string declName, int version, bool versioned)
    {
        string dir = Combine(output, Combine("schema", ComposeDirectory(dslNamespace)));
        string file = versioned
            ? declName + ".v" + version.ToString(CultureInfo.InvariantCulture) + ".sql"
            : declName + ".sql";
        return Combine(dir, file);
    }

    /// <summary>
    /// One pass over <c>model.Declarations</c> producing the set of FQNs that
    /// have more than one Version in scope (FR-425 / FR-435 multi-version
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
    /// Group declarations by FQN preserving overall ordinal-FQN ordering and
    /// per-group ascending-version ordering. Used by <see cref="Emit"/> to
    /// drive schema/ + migrations/ emission together.
    /// </summary>
    private static SortedDictionary<string, List<(DeclKey Key, TopLevelDecl Decl)>> GroupByFqn(ResolvedModel model)
    {
        var groups = new SortedDictionary<string, List<(DeclKey, TopLevelDecl)>>(StringComparer.Ordinal);
        foreach (var kv in model.Declarations)
        {
            if (!groups.TryGetValue(kv.Key.Fqn, out var list))
            {
                list = new List<(DeclKey, TopLevelDecl)>();
                groups[kv.Key.Fqn] = list;
            }
            list.Add((kv.Key, kv.Value));
        }
        // model.Declarations already iterates in (FQN, Version asc) order so
        // per-group lists are naturally version-ascending — but be explicit
        // for defence-in-depth.
        foreach (var list in groups.Values)
        {
            list.Sort((a, b) => a.Item1.Version.CompareTo(b.Item1.Version));
        }
        return groups;
    }

    /// <summary>
    /// Map declaration FQN → SourceFile. Identical helper shape to
    /// <c>JsonSchemaEmitter.BuildDeclToFile</c>; duplicated intentionally (the
    /// PostgreSQL DDL emitter must not link <c>Gravity.Dsl.Emitter.JsonSchema</c>
    /// per FR-400 / FR-463).
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
    /// <c>JsonSchemaEmitter.ComposeDirectory</c>.
    /// </summary>
    internal static string ComposeDirectory(string? dslNamespace)
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
    /// path-traversal or path-injection artifacts.
    /// </summary>
    internal static void ValidateNamespaceSegment(string segment)
    {
        if (segment.Length == 0 || segment == ".." || segment.Contains('/') || segment.Contains('\\'))
        {
            throw new InvalidOperationException(
                "Namespace segment '" + segment + "' is not a safe path component.");
        }
    }

    /// <summary>
    /// Join two relative-path components with a forward slash. Normalises any
    /// embedded back-slashes to forward slashes; collapses empty components.
    /// </summary>
    internal static string Combine(string dir, string name)
    {
        if (string.IsNullOrEmpty(dir)) return name.Replace('\\', '/');
        if (string.IsNullOrEmpty(name)) return dir.Replace('\\', '/');
        return (dir + "/" + name).Replace('\\', '/');
    }
}
