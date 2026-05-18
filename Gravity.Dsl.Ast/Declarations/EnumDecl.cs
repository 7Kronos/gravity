using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// A user-defined enum declaration (<c>enum Name { A, B, C }</c>). Variants are
/// stored in declaration order (FR-013).
/// </summary>
public sealed record EnumDecl(
    string Name,
    int Version,
    ImmutableArray<string> Variants,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span)
    : TopLevelDecl(Name, Version, Span);
