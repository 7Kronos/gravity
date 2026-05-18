namespace Gravity.Dsl.Compiler.Parsing;

/// <summary>
/// Stable rule-id strings emitted by the parser. Most parse-level rule strings
/// are inlined at their throw site; the constants here are reserved for rules
/// referenced from more than one production or by tests.
/// </summary>
internal static class RuleIds
{
    /// <summary>Maximum recursive descent nesting depth exceeded (DoS guard).</summary>
    public const string Parse010 = "PARSE010";

    /// <summary>The compile-time depth cap enforced by the parser.</summary>
    public const int MaxDepth = 256;
}
