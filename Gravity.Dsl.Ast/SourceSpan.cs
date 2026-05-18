namespace Gravity.Dsl.Ast;

/// <summary>
/// Location in a Gravity source file. Used by every AST node and every diagnostic.
/// </summary>
/// <param name="Path">Path to the source file, as supplied to the parser.</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number.</param>
/// <param name="Length">Length of the span in characters; may be 0 for a point span.</param>
public sealed record SourceSpan(string Path, int Line, int Column, int Length);
