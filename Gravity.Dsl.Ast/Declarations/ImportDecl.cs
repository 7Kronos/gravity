namespace Gravity.Dsl.Ast;

/// <summary>
/// An <c>import "relative/path.gravity";</c> declaration. The path is recorded
/// verbatim; cycle detection and resolution live in the resolver (FR-061, FR-062).
/// </summary>
public sealed record ImportDecl(string RelativePath, SourceSpan Span);
