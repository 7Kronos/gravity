namespace Gravity.Dsl.Ast;

/// <summary>
/// A reference to a built-in primitive type. Per FR-011, both <c>?</c> and <c>[]</c>
/// modifiers may apply, and <c>String[]?</c> versus <c>String?[]</c> are distinct.
/// </summary>
public sealed record PrimitiveTypeRef(
    PrimitiveKind Kind,
    bool IsOptional,
    bool IsArray,
    SourceSpan Span)
    : TypeRef(Span);
