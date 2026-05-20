namespace Gravity.Dsl.Emitter.PostgresDdl;

/// <summary>
/// Rule id constants for diagnostics raised by the PostgreSQL DDL emitter.
/// Mirrors the per-emitter constants pattern established by
/// <c>Gravity.Dsl.Emitter.JsonSchema.JsonRuleIds</c>; these ids ship with the
/// emitter assembly only and are NOT added to
/// <c>Gravity.Dsl.Compiler/RuleIds.cs</c>. See spec FR-464.
/// </summary>
internal static class PgRuleIds
{
    /// <summary>Invalid <c>schema</c> config value (not a valid PG identifier per <c>[a-z_][a-z0-9_]*</c>, length ≤ 63). Severity Error. FR-470 / FR-464.</summary>
    public const string Pg001 = "PG001";

    /// <summary>Entity declares a property named <c>state</c> that collides with the reserved lifecycle-state column. Severity Error. FR-424 / FR-464.</summary>
    public const string Pg002 = "PG002";

    /// <summary>Unknown <c>@postgres</c> annotation key, annotation value-type mismatch, or reserved-but-not-consumed key (<c>precision</c>, <c>scale</c>, <c>max_length</c>, <c>storage</c>, <c>index_method</c>, <c>partial_where</c>). Severity Error. FR-441 / FR-464.</summary>
    public const string Pg003 = "PG003";

    /// <summary><c>@postgres(column: "&lt;value&gt;")</c> override is not a valid PG identifier. Severity Error. FR-441 / FR-464.</summary>
    public const string Pg004 = "PG004";

    // PG005..PG010 reserved for forward use; not consumed by this slice.
}
