using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// A line in an entity's <c>commands</c> block. The <see cref="ReturnsType"/> and
/// <see cref="SideEffectEvent"/> sub-clauses are both mandatory per FR-026; the
/// resolver and validator enforce that both names resolve inside the same entity.
/// </summary>
public sealed record CommandDecl(
    string Name,
    ImmutableArray<FieldDecl> Arguments,
    string ReturnsType,
    string SideEffectEvent,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);
