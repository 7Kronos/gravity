using System.Collections.Immutable;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Emitter.PostgresDdl.Render;

namespace Gravity.Dsl.Emitter.PostgresDdl;

/// <summary>
/// Typed projection of the host-validated <see cref="EmitterConfig"/> for the
/// PostgreSQL DDL emitter. Mirrors the shape of
/// <c>JsonSchemaEmitterConfig.From</c>.
/// </summary>
internal sealed class PostgresDdlEmitterConfig
{
    /// <summary>The relative output directory under the host's <c>outputRoot</c>.</summary>
    public string Output { get; }

    /// <summary>The target PostgreSQL schema. Always a valid PG identifier (validated at construction).</summary>
    public string Schema { get; }

    /// <summary>The migration-filename prefix (default <c>"V"</c>, Flyway-compatible).</summary>
    public string MigrationPrefix { get; }

    private PostgresDdlEmitterConfig(string output, string schema, string migrationPrefix)
    {
        Output = output;
        Schema = schema;
        MigrationPrefix = migrationPrefix;
    }

    /// <summary>
    /// Project + pre-flight. Returns <c>null</c> and appends <c>PG001</c> to
    /// <paramref name="diags"/> when <c>schema</c> is not a valid PG identifier
    /// (FR-402, FR-464, FR-470).
    /// </summary>
    public static PostgresDdlEmitterConfig? From(EmitterConfig config, ImmutableArray<Diagnostic>.Builder diags)
    {
        string output = config.GetString(PostgresDdlEmitter.ConfigKeyOutput);
        string schema = TryGetString(config, PostgresDdlEmitter.ConfigKeySchema) ?? PostgresDdlEmitter.DefaultSchema;
        string prefix = TryGetString(config, PostgresDdlEmitter.ConfigKeyMigrationPrefix) ?? PostgresDdlEmitter.DefaultMigrationPrefix;

        if (!Identifier.IsValidPgIdentifier(schema))
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                PgRuleIds.Pg001,
                "schema name '" + schema + "' is not a valid PostgreSQL identifier (expected [a-z_][a-z0-9_]*, length 1..63)",
                new SourceSpan(PostgresDdlEmitter.TargetNameValue, 1, 1, 0)));
            return null;
        }

        return new PostgresDdlEmitterConfig(output, schema, prefix);
    }

    private static string? TryGetString(EmitterConfig config, string key)
        => config.Values.TryGetValue(key, out var raw) && raw is string s ? s : null;
}
