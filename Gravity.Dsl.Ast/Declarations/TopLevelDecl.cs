namespace Gravity.Dsl.Ast;

/// <summary>
/// Base for top-level declarations: <see cref="EntityDecl"/>, <see cref="ValueTypeDecl"/>,
/// <see cref="EnumDecl"/>. Carries the simple name, the per-declaration version
/// (FR-014, FR-040), and the source span.
/// </summary>
public abstract record TopLevelDecl(string Name, int Version, SourceSpan Span);
