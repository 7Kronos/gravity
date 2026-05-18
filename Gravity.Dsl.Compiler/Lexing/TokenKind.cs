namespace Gravity.Dsl.Compiler.Lexing;

/// <summary>
/// Token kinds produced by <see cref="Lexer"/>. Reserved-word kinds are exhaustive
/// per FR-004. Trivia (whitespace, comments) is consumed by the lexer and never
/// surfaced as a token.
/// </summary>
internal enum TokenKind
{
    // Identifiers and literals
    Identifier,
    IntegerLiteral,
    DecimalLiteral,
    StringLiteral,

    // Punctuation
    LBrace,        // {
    RBrace,        // }
    LParen,        // (
    RParen,        // )
    LBracket,      // [
    RBracket,      // ]
    Semicolon,     // ;
    Comma,         // ,
    Colon,         // :
    Question,      // ?
    Arrow,         // ->
    At,            // @
    Dot,           // .

    // Reserved words (FR-004)
    Namespace,
    Import,
    Entity,
    Type,
    Enum,
    Version,
    Deprecates,
    Until,
    Identity,
    Relations,
    Properties,
    Lifecycle,
    States,
    Transitions,
    On,
    Events,
    Commands,
    Returns,
    With,
    SideEffect,
    Cardinality,
    Semantic,

    // Boolean literals - not in FR-004 reserved list but used in annotation values
    True,
    False,

    EndOfFile
}
