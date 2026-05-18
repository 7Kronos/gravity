namespace Gravity.Dsl.Ast;

/// <summary>
/// Base for a type reference appearing on a field, property, identity, or argument.
/// Concrete forms are <see cref="PrimitiveTypeRef"/> and <see cref="NamedTypeRef"/>.
/// </summary>
public abstract record TypeRef(SourceSpan Span);
