namespace Gravity.Dsl.Cli;

/// <summary>
/// CLI-binary-only rule ids (FR-142). The compiler library's rule-id surface
/// (<c>Gravity.Dsl.Compiler/Validation/RuleIds.cs</c>,
/// <c>Gravity.Dsl.Compiler/Parsing/RuleIds.cs</c>) is intentionally disjoint
/// from this set so analysis tooling can distinguish "compiler said no" from
/// "CLI driver said no".
/// </summary>
public static class CliRuleIds
{
    /// <summary>
    /// Malformed <c>--as-of YYYY-MM-DD</c> value (FR-142). Emitted before any
    /// compilation work begins; the CLI exits non-zero.
    /// </summary>
    public const string Cli002 = "CLI002";
}
