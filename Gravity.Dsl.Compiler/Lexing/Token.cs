using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Lexing;

/// <summary>
/// A single lexical token. <see cref="Lexeme"/> is the verbatim slice from source;
/// for string literals the lexeme excludes the surrounding quotes and has escape
/// sequences resolved (so consumers see the decoded value).
/// </summary>
internal sealed record Token(TokenKind Kind, string Lexeme, SourceSpan Span);
