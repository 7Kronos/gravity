using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// A line in an entity's <c>relations</c> block. The optional <see cref="Semantic"/>
/// identifier records a domain role (e.g. <c>submitted_by</c>) for downstream emitters
/// or governance layers. See FR-022.
/// </summary>
public sealed record RelationDecl(
    string Name,
    string TargetEntity,
    bool IsOptional,
    Cardinality Cardinality,
    string? Semantic,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);
