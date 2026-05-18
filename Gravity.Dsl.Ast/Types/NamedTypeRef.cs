namespace Gravity.Dsl.Ast;

/// <summary>
/// A reference to a user-declared type (a <see cref="ValueTypeDecl"/> or
/// <see cref="EnumDecl"/>) by simple name. The resolver attaches the fully
/// qualified name later; the AST stores the as-written identifier.
/// </summary>
public sealed record NamedTypeRef(
    string Name,
    bool IsOptional,
    bool IsArray,
    SourceSpan Span)
    : TypeRef(Span);
