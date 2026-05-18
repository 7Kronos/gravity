using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// A line in an entity's <c>events</c> block. Empty payloads (<c>EventName {};</c>)
/// are legal and represented by an empty <see cref="Payload"/> array.
/// </summary>
public sealed record EventDecl(
    string Name,
    ImmutableArray<FieldDecl> Payload,
    ImmutableArray<AnnotationDecl> Annotations,
    SourceSpan Span);
