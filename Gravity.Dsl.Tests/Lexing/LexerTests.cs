using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Compiler.Lexing;
using Xunit;

namespace Gravity.Dsl.Tests.Lexing;

public sealed class LexerTests
{
    [Fact]
    public void EntityXVersion1_TokenizesToSixTokens()
    {
        var lex = Lexer.Tokenize("entity X version 1 { }", "test");
        lex.Diagnostics.Should().BeEmpty();
        var kinds = lex.Tokens.Select(t => t.Kind).ToArray();
        kinds.Should().Equal(
            TokenKind.Entity,
            TokenKind.Identifier,
            TokenKind.Version,
            TokenKind.IntegerLiteral,
            TokenKind.LBrace,
            TokenKind.RBrace,
            TokenKind.EndOfFile);
        lex.Tokens[1].Lexeme.Should().Be("X");
        lex.Tokens[3].Lexeme.Should().Be("1");
    }

    [Fact]
    public void AllReservedWords_AreClassified()
    {
        var src = "namespace import entity type enum version deprecates until "
                + "identity relations properties lifecycle states transitions on "
                + "events commands returns with side_effect cardinality semantic";
        var lex = Lexer.Tokenize(src, "test");
        lex.Diagnostics.Should().BeEmpty();
        var kinds = lex.Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        kinds.Should().Equal(
            TokenKind.Namespace, TokenKind.Import, TokenKind.Entity, TokenKind.Type, TokenKind.Enum,
            TokenKind.Version, TokenKind.Deprecates, TokenKind.Until, TokenKind.Identity, TokenKind.Relations,
            TokenKind.Properties, TokenKind.Lifecycle, TokenKind.States, TokenKind.Transitions, TokenKind.On,
            TokenKind.Events, TokenKind.Commands, TokenKind.Returns, TokenKind.With, TokenKind.SideEffect,
            TokenKind.Cardinality, TokenKind.Semantic);
    }

    [Fact]
    public void AllPrimitives_TokenizeAsIdentifiers()
    {
        var src = "String Int Long Decimal Boolean Date DateTime UUID";
        var lex = Lexer.Tokenize(src, "test");
        lex.Diagnostics.Should().BeEmpty();
        var kinds = lex.Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        kinds.Should().AllBeEquivalentTo(TokenKind.Identifier);
        lex.Tokens.Where(t => t.Kind == TokenKind.Identifier).Select(t => t.Lexeme).Should().Equal(
            "String", "Int", "Long", "Decimal", "Boolean", "Date", "DateTime", "UUID");
    }

    [Fact]
    public void Annotation_WithStringAndIntArgs_Tokenizes()
    {
        var src = "@ns(k: \"v\", n: 3)";
        var lex = Lexer.Tokenize(src, "test");
        lex.Diagnostics.Should().BeEmpty();
        var kinds = lex.Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        kinds.Should().Equal(
            TokenKind.At,
            TokenKind.Identifier,
            TokenKind.LParen,
            TokenKind.Identifier,
            TokenKind.Colon,
            TokenKind.StringLiteral,
            TokenKind.Comma,
            TokenKind.Identifier,
            TokenKind.Colon,
            TokenKind.IntegerLiteral,
            TokenKind.RParen);
        lex.Tokens.First(t => t.Kind == TokenKind.StringLiteral).Lexeme.Should().Be("v");
    }

    [Fact]
    public void BothCommentForms_AreSkipped()
    {
        var src = "// hello\nentity /* block */ X version 1 { }";
        var lex = Lexer.Tokenize(src, "test");
        lex.Diagnostics.Should().BeEmpty();
        var kinds = lex.Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        kinds.Should().Equal(
            TokenKind.Entity,
            TokenKind.Identifier,
            TokenKind.Version,
            TokenKind.IntegerLiteral,
            TokenKind.LBrace,
            TokenKind.RBrace);
    }

    [Fact]
    public void UnterminatedString_EmitsLex001()
    {
        var src = "\"unterminated";
        var lex = Lexer.Tokenize(src, "test");
        lex.Diagnostics.Should().HaveCount(1);
        lex.Diagnostics[0].RuleId.Should().Be("LEX001");
        lex.Diagnostics[0].Message.Should().Contain("unterminated string");
    }

    [Fact]
    public void Arrow_TokenizesAsTwoCharOperator()
    {
        var lex = Lexer.Tokenize("A -> B", "test");
        lex.Diagnostics.Should().BeEmpty();
        var kinds = lex.Tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();
        kinds.Should().Equal(TokenKind.Identifier, TokenKind.Arrow, TokenKind.Identifier);
    }

    [Fact]
    public void DecimalLiteral_IsRecognized()
    {
        var lex = Lexer.Tokenize("1.5", "test");
        lex.Diagnostics.Should().BeEmpty();
        lex.Tokens[0].Kind.Should().Be(TokenKind.DecimalLiteral);
        lex.Tokens[0].Lexeme.Should().Be("1.5");
    }

    [Fact]
    public void UnknownCharacter_EmitsLex001()
    {
        var lex = Lexer.Tokenize("#", "test");
        lex.Diagnostics.Should().HaveCount(1);
        lex.Diagnostics[0].RuleId.Should().Be("LEX001");
    }

    [Fact]
    public void At_Produces_AtToken()
    {
        // Phase 8 / T103: bare '@' must tokenize to TokenKind.At so the parser can
        // consume an '@N' version suffix on a type ref independently of annotation
        // contexts. Pinned in isolation (the annotation tests already exercise '@'
        // followed by an identifier; this case asserts the lone punctuation form).
        var lex = Lexer.Tokenize("@", "test");
        lex.Tokens[0].Kind.Should().Be(TokenKind.At);
    }

    [Fact]
    public void UnknownStringEscape_Emits_LEX002()
    {
        // \x is not in the supported escape set {\\, \", \n, \t, \r}.
        var lex = Lexer.Tokenize("\"a\\xb\"", "test");
        lex.Diagnostics.Should().Contain(d => d.RuleId == "LEX002"
            && d.Message.Contains("\\x"));
        // The token after recovery should still capture both surrounding characters
        // without re-interpreting 'x' as a literal trailing character.
        lex.Tokens[0].Kind.Should().Be(TokenKind.StringLiteral);
        lex.Tokens[0].Lexeme.Should().Be("axb");
    }
}
