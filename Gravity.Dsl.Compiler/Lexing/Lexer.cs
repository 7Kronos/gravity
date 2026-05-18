using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Lexing;

/// <summary>
/// Hand-written tokenizer. Single-pass over the source string, tracks line/column,
/// skips whitespace and both comment forms (<c>//</c>, <c>/* */</c>), and emits
/// tokens for every reserved word listed in FR-004 plus identifiers, integer/decimal
/// literals, string literals, and punctuation.
/// </summary>
internal static class Lexer
{
    /// <summary>
    /// Tokenize <paramref name="source"/> from <paramref name="path"/>. Lexical errors
    /// (unknown character, unterminated string, unterminated block comment) are
    /// returned as <see cref="Diagnostic"/> entries with rule <c>LEX001</c>; the
    /// tokenizer recovers and continues so callers see as many issues as possible.
    /// </summary>
    public static LexResult Tokenize(string source, string path)
    {
        var tokens = ImmutableArray.CreateBuilder<Token>();
        var diagnostics = new List<Diagnostic>();

        int index = 0;
        int line = 1;
        int column = 1;
        int length = source.Length;

        while (index < length)
        {
            char c = source[index];

            // Whitespace
            if (c == ' ' || c == '\t' || c == '\r')
            {
                index++;
                column++;
                continue;
            }
            if (c == '\n')
            {
                index++;
                line++;
                column = 1;
                continue;
            }

            // Comments
            if (c == '/' && index + 1 < length && source[index + 1] == '/')
            {
                while (index < length && source[index] != '\n')
                {
                    index++;
                    column++;
                }
                continue;
            }
            if (c == '/' && index + 1 < length && source[index + 1] == '*')
            {
                int startLine = line;
                int startCol = column;
                index += 2;
                column += 2;
                bool closed = false;
                while (index < length)
                {
                    if (source[index] == '*' && index + 1 < length && source[index + 1] == '/')
                    {
                        index += 2;
                        column += 2;
                        closed = true;
                        break;
                    }
                    if (source[index] == '\n')
                    {
                        index++;
                        line++;
                        column = 1;
                    }
                    else
                    {
                        index++;
                        column++;
                    }
                }
                if (!closed)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "LEX001",
                        "unterminated block comment",
                        new SourceSpan(path, startLine, startCol, 2)));
                }
                continue;
            }

            int tokenLine = line;
            int tokenColumn = column;
            int startIndex = index;

            // Identifier / reserved word
            if (IsIdentifierStart(c))
            {
                int begin = index;
                while (index < length && IsIdentifierPart(source[index]))
                {
                    index++;
                    column++;
                }
                string lexeme = source.Substring(begin, index - begin);
                TokenKind kind = ClassifyIdentifier(lexeme);
                tokens.Add(new Token(kind, lexeme, new SourceSpan(path, tokenLine, tokenColumn, index - begin)));
                continue;
            }

            // Numeric literal (integer or decimal). No sign here; '-' is not legal in any
            // FR-004 context except inside an arrow '->' which is handled below.
            if (IsDigit(c))
            {
                int begin = index;
                while (index < length && IsDigit(source[index]))
                {
                    index++;
                    column++;
                }
                bool isDecimal = false;
                if (index < length && source[index] == '.' && index + 1 < length && IsDigit(source[index + 1]))
                {
                    isDecimal = true;
                    index++;
                    column++;
                    while (index < length && IsDigit(source[index]))
                    {
                        index++;
                        column++;
                    }
                }
                string lexeme = source.Substring(begin, index - begin);
                tokens.Add(new Token(
                    isDecimal ? TokenKind.DecimalLiteral : TokenKind.IntegerLiteral,
                    lexeme,
                    new SourceSpan(path, tokenLine, tokenColumn, index - begin)));
                continue;
            }

            // String literal: "..." with \" and \\ escapes
            if (c == '"')
            {
                index++;
                column++;
                var sb = new StringBuilder();
                bool closed = false;
                while (index < length)
                {
                    char ch = source[index];
                    if (ch == '"')
                    {
                        index++;
                        column++;
                        closed = true;
                        break;
                    }
                    if (ch == '\\' && index + 1 < length)
                    {
                        char next = source[index + 1];
                        if (next == '"' || next == '\\')
                        {
                            sb.Append(next);
                            index += 2;
                            column += 2;
                            continue;
                        }
                        if (next == 'n') { sb.Append('\n'); index += 2; column += 2; continue; }
                        if (next == 't') { sb.Append('\t'); index += 2; column += 2; continue; }
                        if (next == 'r') { sb.Append('\r'); index += 2; column += 2; continue; }
                        // Unknown escape: report LEX002, preserve the offending char in the
                        // token text so spans stay aligned, and advance past both chars so
                        // the scanner does not re-read the escape body as a literal char.
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            "LEX002",
                            "unknown string escape sequence '\\" + next + "'",
                            new SourceSpan(path, line, column, 2)));
                        sb.Append(next);
                        index += 2;
                        column += 2;
                        continue;
                    }
                    if (ch == '\n')
                    {
                        // Unterminated string literal — strings do not span lines.
                        break;
                    }
                    sb.Append(ch);
                    index++;
                    column++;
                }
                if (!closed)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "LEX001",
                        "unterminated string literal",
                        new SourceSpan(path, tokenLine, tokenColumn, index - startIndex)));
                }
                tokens.Add(new Token(
                    TokenKind.StringLiteral,
                    sb.ToString(),
                    new SourceSpan(path, tokenLine, tokenColumn, index - startIndex)));
                continue;
            }

            // Two-character operator '->'
            if (c == '-' && index + 1 < length && source[index + 1] == '>')
            {
                tokens.Add(new Token(TokenKind.Arrow, "->", new SourceSpan(path, tokenLine, tokenColumn, 2)));
                index += 2;
                column += 2;
                continue;
            }

            // Single-character punctuation
            TokenKind? punct = c switch
            {
                '{' => TokenKind.LBrace,
                '}' => TokenKind.RBrace,
                '(' => TokenKind.LParen,
                ')' => TokenKind.RParen,
                '[' => TokenKind.LBracket,
                ']' => TokenKind.RBracket,
                ';' => TokenKind.Semicolon,
                ',' => TokenKind.Comma,
                ':' => TokenKind.Colon,
                '?' => TokenKind.Question,
                '@' => TokenKind.At,
                '.' => TokenKind.Dot,
                _ => null
            };
            if (punct is { } kindPunct)
            {
                tokens.Add(new Token(kindPunct, c.ToString(CultureInfo.InvariantCulture),
                    new SourceSpan(path, tokenLine, tokenColumn, 1)));
                index++;
                column++;
                continue;
            }

            // Unknown character: emit a diagnostic and skip.
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "LEX001",
                "unexpected character '" + c + "'",
                new SourceSpan(path, tokenLine, tokenColumn, 1)));
            index++;
            column++;
        }

        tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, new SourceSpan(path, line, column, 0)));
        return new LexResult(tokens.ToImmutable(), diagnostics);
    }

    private static bool IsIdentifierStart(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c) || IsDigit(c);

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static TokenKind ClassifyIdentifier(string lexeme) => lexeme switch
    {
        "namespace" => TokenKind.Namespace,
        "import" => TokenKind.Import,
        "entity" => TokenKind.Entity,
        "type" => TokenKind.Type,
        "enum" => TokenKind.Enum,
        "version" => TokenKind.Version,
        "deprecates" => TokenKind.Deprecates,
        "until" => TokenKind.Until,
        "identity" => TokenKind.Identity,
        "relations" => TokenKind.Relations,
        "properties" => TokenKind.Properties,
        "lifecycle" => TokenKind.Lifecycle,
        "states" => TokenKind.States,
        "transitions" => TokenKind.Transitions,
        "on" => TokenKind.On,
        "events" => TokenKind.Events,
        "commands" => TokenKind.Commands,
        "returns" => TokenKind.Returns,
        "with" => TokenKind.With,
        "side_effect" => TokenKind.SideEffect,
        "cardinality" => TokenKind.Cardinality,
        "semantic" => TokenKind.Semantic,
        "true" => TokenKind.True,
        "false" => TokenKind.False,
        _ => TokenKind.Identifier
    };
}

/// <summary>
/// Result of <see cref="Lexer.Tokenize"/>: ordered tokens and lexical diagnostics.
/// </summary>
internal sealed record LexResult(ImmutableArray<Token> Tokens, IReadOnlyList<Diagnostic> Diagnostics);
