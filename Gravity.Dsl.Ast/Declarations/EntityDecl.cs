using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// An <c>entity</c> declaration: identity, relations, properties, lifecycle, events,
/// and commands. See FR-020 et seq. for the surface contract.
/// </summary>
public sealed record EntityDecl(
    string Name,
    int Version,
    DeprecatesClause? Deprecates,
    IdentityDecl Identity,
    ImmutableArray<RelationDecl> Relations,
    ImmutableArray<PropertyDecl> Properties,
    LifecycleDecl Lifecycle,
    ImmutableArray<EventDecl> Events,
    ImmutableArray<CommandDecl> Commands,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span)
    : TopLevelDecl(Name, Version, Span);
