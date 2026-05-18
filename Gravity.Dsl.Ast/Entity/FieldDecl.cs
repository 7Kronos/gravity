namespace Gravity.Dsl.Ast;

/// <summary>
/// A simple <c>name: TypeRef;</c> field used by value-type bodies, event payloads,
/// and command argument lists. Annotations are not permitted on these positions in
/// Phase 0–3 (FR-053 reserves them for future use elsewhere).
/// </summary>
public sealed record FieldDecl(string Name, TypeRef Type, SourceSpan Span);
