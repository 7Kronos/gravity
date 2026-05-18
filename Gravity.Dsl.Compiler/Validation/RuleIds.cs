namespace Gravity.Dsl.Compiler.Validation;

/// <summary>
/// Stable rule-id strings used by the validator. Keep these in sync with
/// tasks.md and the spec; emitter-host rules live in
/// <c>Gravity.Dsl.Emitter</c> (Phase 2) and are not duplicated here.
/// </summary>
internal static class RuleIds
{
    public const string Val001 = "VAL001"; // FR-030 transition state not in states {}
    public const string Val002 = "VAL002"; // FR-031 transition event not in events {}
    public const string Val003 = "VAL003"; // FR-032 command side_effect event not in events {}
    public const string Val004 = "VAL004"; // FR-033 declared state with no incoming transition (warning)
    public const string Val005 = "VAL005"; // FR-021 identity is not UUID (warning)
    public const string Val006 = "VAL006"; // FR-051 annotation namespace not claimed
    public const string Val009 = "VAL009"; // FR-041 deprecates date format
    public const string Val010 = "VAL010"; // FR-022 optional + many is forbidden

    // Phase 8 (P8c) breaking-change rules. See FR-130..FR-138, FR-151.
    public const string Val020 = "VAL020"; // FR-130 field removed (entity property / value-type field / event payload)
    public const string Val021 = "VAL021"; // FR-131 type narrowed on surviving field
    public const string Val022 = "VAL022"; // FR-132 lifecycle state removed
    public const string Val023 = "VAL023"; // FR-133 command removed
    public const string Val024 = "VAL024"; // FR-134 event removed
    public const string Val025 = "VAL025"; // FR-135 lifecycle transition removed (warning)
    public const string Val026 = "VAL026"; // FR-136 command argument breaking change
    public const string Val027 = "VAL027"; // FR-137 deprecates chain broken (skipped link)
    public const string Val028 = "VAL028"; // FR-124 deprecates names non-existent version
    public const string Val029 = "VAL029"; // FR-125 deprecates self / forward reference
    public const string Val030 = "VAL030"; // FR-138 deprecation window expired
}
