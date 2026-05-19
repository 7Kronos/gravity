namespace Gravity.Dsl.Emitter.JsonSchema;

/// <summary>
/// Stable rule-id strings emitted by the JSON Schema emitter. Per the Phase 9
/// MSB-emitter precedent these constants ship with the emitter assembly only —
/// they are NOT added to the compiler library's RuleIds.cs, so a third-party
/// emitter that links Gravity.Dsl.Emitter does not see (or accidentally
/// redefine) these ids. Public so tests and downstream consumers can reference
/// the well-known rule strings by name rather than by literal.
/// </summary>
public static class JsonRuleIds
{
    /// <summary>
    /// Unknown <c>@json_schema</c> annotation key, OR annotation value type
    /// mismatch (FR-341), OR <c>multipleOf: 0</c> (Draft-07 forbids it).
    /// Severity: Error. FR-341, FR-342, FR-364.
    /// </summary>
    public const string Js001 = "JS001";

    /// <summary>
    /// <c>bundle_strategy</c> set to a value other than <c>"per-entity"</c>.
    /// Severity: Error. FR-302, FR-364.
    /// </summary>
    public const string Js002 = "JS002";

    /// <summary>
    /// User-declared property name collides with the reserved <c>state</c>
    /// property on the entity-state schema. Severity: Error. FR-315, FR-364.
    /// </summary>
    public const string Js003 = "JS003";

    /// <summary>
    /// <c>@json_schema(format: "&lt;value&gt;")</c> carries a format string not
    /// in the emitter's known set (FR-341 known set: email, uri, uuid, date,
    /// date-time, time, hostname, ipv4, ipv6, regex, decimal). Severity:
    /// Warning. FR-341, FR-364.
    /// </summary>
    public const string Js004 = "JS004";

    /// <summary>Reserved for forward use.</summary>
    public const string Js005 = "JS005";

    /// <summary>Reserved for forward use.</summary>
    public const string Js006 = "JS006";

    /// <summary>Reserved for forward use.</summary>
    public const string Js007 = "JS007";

    /// <summary>Reserved for forward use.</summary>
    public const string Js008 = "JS008";

    /// <summary>Reserved for forward use.</summary>
    public const string Js009 = "JS009";

    /// <summary>Reserved for forward use.</summary>
    public const string Js010 = "JS010";
}
