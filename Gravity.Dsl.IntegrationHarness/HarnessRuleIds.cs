namespace Gravity.Dsl.IntegrationHarness;

/// <summary>
/// Diagnostic-rule constants for the integration harness (FR-3050).
/// Each constant names the AC it pins and the failure scenario it covers.
/// </summary>
public static class HarnessRuleIds
{
    /// <summary>HARN001 — ItemMetadataOverride harness subcommand failed (FR-3010, AC-9.11).</summary>
    public const string Harn001 = "HARN001";

    /// <summary>HARN002 — HookOrder harness subcommand failed: GravityDslGenerate target not found in build log (FR-3011, AC-9.12).</summary>
    public const string Harn002 = "HARN002";

    /// <summary>HARN003 — EmptyInput harness subcommand failed: unexpected diagnostics or Generated/ tree present (FR-3012, AC-9.13).</summary>
    public const string Harn003 = "HARN003";

    /// <summary>HARN004 — NoGlobalTool harness subcommand failed: gravc is installed globally or step-2 build failed (FR-3013, AC-9.14).</summary>
    public const string Harn004 = "HARN004";

    /// <summary>HARN005 — IncrementalBuild harness subcommand failed: build step exited non-zero (FR-3014, AC-9.15).</summary>
    public const string Harn005 = "HARN005";

    /// <summary>HARN006 — IncrementalBuild harness subcommand failed: first build did not execute GravityDslGenTask (FR-3014, AC-9.15).</summary>
    public const string Harn006 = "HARN006";

    /// <summary>HARN007 — IncrementalBuild harness subcommand failed: second build (no source change) still ran GravityDslGenTask (FR-3014, AC-9.15).</summary>
    public const string Harn007 = "HARN007";

    /// <summary>HARN008 — IncrementalBuild harness subcommand failed: third build (after touch) did not re-run GravityDslGenTask (FR-3014, AC-9.15).</summary>
    public const string Harn008 = "HARN008";

    /// <summary>HARN009 — PackDeterminism harness subcommand failed: normalised .nupkg SHA-256 hashes are not equal across two packs (FR-3015, AC-9.7-pack).</summary>
    public const string Harn009 = "HARN009";

    /// <summary>HARN010 — Reserved for forward use.</summary>
    public const string Harn010 = "HARN010";
}
