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
}
