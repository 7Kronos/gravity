namespace Gravity.Dsl.MsBuild;

/// <summary>
/// MSBuild-host rule ids reserved by Phase 9 (FR-242). The MSBuild host rule
/// namespace is deliberately separate from the compiler-library rule namespaces
/// (PARSE…, LEX…, RES…, VAL…, HOST…, CFG…) so a third-party emitter that links
/// the compiler library does not see (or accidentally redefine) MSB ids.
/// </summary>
public static class MsBuildRuleIds
{
    /// <summary>
    /// <c>&lt;GravityDslAsOf&gt;</c> malformed (FR-233 / FR-242). Counterpart to
    /// <c>CliRuleIds.Cli002</c> on the MSBuild surface — both fire for malformed
    /// <c>YYYY-MM-DD</c> values before any compilation work begins.
    /// </summary>
    public const string Msb001 = "MSB001";

    /// <summary>Reserved for Phase 9b — exact semantics TBD (FR-242).</summary>
    public const string Msb002 = "MSB002";

    /// <summary><c>&lt;GravityDslConfig&gt;</c> names a file that does not exist on disk (FR-203 / FR-242).</summary>
    public const string Msb003 = "MSB003";

    /// <summary>Reserved for Phase 9b — exact semantics TBD (FR-242).</summary>
    public const string Msb004 = "MSB004";

    /// <summary>Reserved for Phase 9b — exact semantics TBD (FR-242).</summary>
    public const string Msb005 = "MSB005";

    /// <summary>Reserved for Phase 9b — exact semantics TBD (FR-242).</summary>
    public const string Msb006 = "MSB006";

    /// <summary>Reserved for Phase 9b — exact semantics TBD (FR-242).</summary>
    public const string Msb007 = "MSB007";

    // Msb008..Msb010 reserved per FR-242.
}
