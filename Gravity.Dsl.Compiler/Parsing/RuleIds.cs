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

    /// <summary>
    /// Malformed or illegally-positioned <c>@N</c> version suffix on a type ref
    /// (Phase 8, FR-100/FR-101). Fires on missing/non-positive literal, leading
    /// zero, whitespace gap, or <c>@N</c> after a primitive type, a relation
    /// target, or a command <c>returns</c> clause. Also fires when a bare
    /// integer literal used as an entity/value-type/enum <c>version</c> number
    /// overflows <see cref="int.MaxValue"/> — keeping all "positive-integer
    /// version expected here" failures under a single id.
    /// </summary>
    public const string Parse020 = "PARSE020";

    /// <summary>
    /// Annotation integer-literal argument exceeds <see cref="long.MaxValue"/>
    /// (or otherwise fails to parse as a 64-bit signed integer). Distinct from
    /// PARSE020 because annotation values have a different valid range than
    /// version numbers and produce a different recovery (the argument is
    /// dropped rather than coerced).
    /// </summary>
    public const string Parse021 = "PARSE021";

    /// <summary>The compile-time depth cap enforced by the parser.</summary>
    public const int MaxDepth = 256;
}
