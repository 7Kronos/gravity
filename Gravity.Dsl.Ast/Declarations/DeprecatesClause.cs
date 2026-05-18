namespace Gravity.Dsl.Ast;

/// <summary>
/// <c>deprecates version &lt;int&gt; until "&lt;ISO-8601&gt;"</c> clause attached to an
/// entity that introduces a breaking change against a prior version (FR-041). The
/// date is validated by <c>VAL009</c>; Phase 0–3 records it but does not enforce
/// the deprecation window (Phase 8).
/// </summary>
public sealed record DeprecatesClause(int Version, string UntilIso8601, SourceSpan Span);
