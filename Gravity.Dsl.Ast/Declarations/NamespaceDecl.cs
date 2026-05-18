namespace Gravity.Dsl.Ast;

/// <summary>
/// A <c>namespace dotted.identifier;</c> declaration. Optional per FR-060;
/// absent files have a null Namespace on <see cref="SourceFile"/>.
/// </summary>
public sealed record NamespaceDecl(string Name, SourceSpan Span);
