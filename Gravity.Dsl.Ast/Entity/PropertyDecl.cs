using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// A line in an entity's <c>properties</c> block. Annotations are namespaced and
/// validated by FR-051; their argument map ordering is enforced by
/// <see cref="AnnotationDecl"/>'s use of <see cref="ImmutableSortedDictionary{TKey, TValue}"/>.
/// </summary>
public sealed record PropertyDecl(
    string Name,
    TypeRef Type,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);
