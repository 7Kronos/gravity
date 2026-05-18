using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// A user-defined value type declaration (<c>type Name { field: TypeRef; ... }</c>).
/// Per FR-012, fields are an ordered list and order is preserved in emitter output.
/// </summary>
public sealed record ValueTypeDecl(
    string Name,
    int Version,
    ImmutableArray<FieldDecl> Fields,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span)
    : TopLevelDecl(Name, Version, Span);
