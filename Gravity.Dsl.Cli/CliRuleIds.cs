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
    /// I/O failure surfaced by the CLI driver — input directory/file does not
    /// exist, output directory cannot be created, etc. Emitted with exit code 3.
    /// Pre-Phase 9 the CLI used <c>CLI001</c> for both malformed-args and I/O
    /// failures; <see cref="Cli002"/> handled the <c>--as-of</c> case specially.
    /// </summary>
    public const string Cli001 = "CLI001";

    /// <summary>
    /// Malformed <c>--as-of YYYY-MM-DD</c> value (FR-142). Emitted before any
    /// compilation work begins; the CLI exits with code 2 (CLI usage error).
    /// </summary>
    public const string Cli002 = "CLI002";

    /// <summary>
    /// Conflicting CLI flags — currently <c>--verbose</c> and <c>--quiet</c>
    /// passed together. Exits with code 2 (CLI usage error).
    /// </summary>
    public const string Cli003 = "CLI003";
}
