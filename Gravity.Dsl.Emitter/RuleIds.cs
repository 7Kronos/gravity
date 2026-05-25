namespace Gravity.Dsl.Emitter;

/// <summary>
/// Stable rule-id strings emitted by the emitter host and config loader.
/// HOST00x — emitter-host diagnostics (Phase 2, plan.md §3.6).
/// CFG00x  — Gravity config loader diagnostics.
/// </summary>
internal static class RuleIds
{
    /// <summary>Emitter declares a SupportedAstVersions range that excludes <c>AstVersion.Value</c>.</summary>
    public const string Host001 = "HOST001";

    /// <summary>Two registered emitters claim the same annotation namespace (FR-052).</summary>
    public const string Host002 = "HOST002";

    /// <summary>Two enabled emitters configured with the same output directory.</summary>
    public const string Host003 = "HOST003";

    /// <summary>Unknown top-level key in the emitter config file (warning).</summary>
    public const string Cfg001 = "CFG001";

    /// <summary>Type mismatch between configured value and schema (error).</summary>
    public const string Cfg002 = "CFG002";

    /// <summary>Required key missing from an emitter's configuration block (error).</summary>
    public const string Cfg003 = "CFG003";

    /// <summary>Emitter <c>output</c> path is rooted or escapes the configured output root (error).</summary>
    public const string Cfg004 = "CFG004";

    /// <summary>Legacy <c>.gravity.config</c> filename is in use; rename to <c>.gravity.yaml</c> (warning).</summary>
    public const string Cfg005 = "CFG005";
}
