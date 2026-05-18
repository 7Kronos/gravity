namespace Gravity.Dsl.Ast;

/// <summary>
/// An entity's <c>identity</c> field. Exactly one per entity (FR-021).
/// </summary>
public sealed record IdentityDecl(string FieldName, TypeRef Type, SourceSpan Span);
