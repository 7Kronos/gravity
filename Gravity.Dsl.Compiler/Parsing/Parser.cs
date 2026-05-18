using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Lexing;

namespace Gravity.Dsl.Compiler.Parsing;

/// <summary>
/// Recursive-descent parser over the <see cref="Lexer"/> token stream. Produces a
/// <see cref="SourceFile"/> AST plus an ordered list of diagnostics. Errors are
/// reported with `path:line:col` via <see cref="SourceSpan"/> on every diagnostic.
/// </summary>
/// <remarks>
/// The plan calls for Pidgin combinators; a hand-written recursive-descent parser is
/// substituted here because it produces equivalent ASTs with significantly less
/// scaffolding for this grammar size and gives precise control over diagnostics.
/// Pidgin remains a project dependency so future grammar work can use it.
/// </remarks>
public static class Parser
{
    public static ParseResult Parse(string path, string source)
    {
        var lex = Lexer.Tokenize(source, path);
        var diagnostics = new List<Diagnostic>(lex.Diagnostics);
        var state = new ParserState(lex.Tokens, path, diagnostics);

        SourceFile? file;
        try
        {
            file = ParseSourceFile(state);
        }
        catch (ParseException ex)
        {
            diagnostics.Add(ex.Diagnostic);
            file = null;
        }
        return new ParseResult(file, diagnostics);
    }

    /// <summary>
    /// Test-only entry point that exercises the depth guard with a configurable cap.
    /// The compile-time cap remains <see cref="RuleIds.MaxDepth"/>; this overload is
    /// reserved for verifying that the recursive-descent guard fires once depth
    /// exceeds the cap, regardless of grammar reach.
    /// </summary>
    internal static ParseResult ParseWithDepthCap(string path, string source, int maxDepth)
    {
        var lex = Lexer.Tokenize(source, path);
        var diagnostics = new List<Diagnostic>(lex.Diagnostics);
        var state = new ParserState(lex.Tokens, path, diagnostics) { MaxDepthOverride = maxDepth };

        SourceFile? file;
        try
        {
            file = ParseSourceFile(state);
        }
        catch (ParseException ex)
        {
            diagnostics.Add(ex.Diagnostic);
            file = null;
        }
        return new ParseResult(file, diagnostics);
    }

    private static SourceFile ParseSourceFile(ParserState s)
    {
        s.EnterDepth();
        try
        {
            NamespaceDecl? ns = null;
            if (s.Peek().Kind == TokenKind.Namespace)
            {
                ns = ParseNamespace(s);
            }

            var imports = ImmutableArray.CreateBuilder<ImportDecl>();
            while (s.Peek().Kind == TokenKind.Import)
            {
                imports.Add(ParseImport(s));
            }

            var decls = ImmutableArray.CreateBuilder<TopLevelDecl>();
            while (s.Peek().Kind != TokenKind.EndOfFile)
            {
                // Annotations on top-level declarations (FR-053). Parse but attach to the
                // following declaration's Annotations array.
                var leading = ParseAnnotations(s);
                switch (s.Peek().Kind)
                {
                    case TokenKind.Entity:
                        decls.Add(ParseEntity(s, leading));
                        break;
                    case TokenKind.Type:
                        decls.Add(ParseValueType(s, leading));
                        break;
                    case TokenKind.Enum:
                        decls.Add(ParseEnum(s, leading));
                        break;
                    default:
                        throw s.ErrorHere("PARSE001", "expected 'entity', 'type', or 'enum' at top level");
                }
            }

            return new SourceFile(s.Path, ns, imports.ToImmutable(), decls.ToImmutable());
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static NamespaceDecl ParseNamespace(ParserState s)
    {
        var nsTok = s.Expect(TokenKind.Namespace);
        var first = s.Expect(TokenKind.Identifier);
        var name = first.Lexeme;
        while (s.Peek().Kind == TokenKind.Dot)
        {
            s.Consume();
            var next = s.Expect(TokenKind.Identifier);
            name = name + "." + next.Lexeme;
        }
        s.Expect(TokenKind.Semicolon);
        return new NamespaceDecl(name, nsTok.Span);
    }

    private static ImportDecl ParseImport(ParserState s)
    {
        var importTok = s.Expect(TokenKind.Import);
        var path = s.Expect(TokenKind.StringLiteral);
        s.Expect(TokenKind.Semicolon);
        return new ImportDecl(path.Lexeme, importTok.Span);
    }

    private static EntityDecl ParseEntity(ParserState s, ImmutableArray<AnnotationDecl> annotations)
    {
        s.EnterDepth();
        try
        {
        var entityTok = s.Expect(TokenKind.Entity);
        var nameTok = s.Expect(TokenKind.Identifier);
        s.Expect(TokenKind.Version);
        int version = ParseInt(s);
        DeprecatesClause? deprecates = null;
        if (s.Peek().Kind == TokenKind.Deprecates)
        {
            var depTok = s.Consume();
            s.Expect(TokenKind.Version);
            int depVersion = ParseInt(s);
            s.Expect(TokenKind.Until);
            var dateTok = s.Expect(TokenKind.StringLiteral);
            deprecates = new DeprecatesClause(depVersion, dateTok.Lexeme, depTok.Span);
        }
        s.Expect(TokenKind.LBrace);

        IdentityDecl? identity = null;
        ImmutableArray<RelationDecl>? relations = null;
        ImmutableArray<PropertyDecl>? properties = null;
        LifecycleDecl? lifecycle = null;
        ImmutableArray<EventDecl>? events = null;
        ImmutableArray<CommandDecl>? commands = null;

        while (s.Peek().Kind != TokenKind.RBrace)
        {
            var tok = s.Peek();
            switch (tok.Kind)
            {
                case TokenKind.Identity:
                    if (identity is not null)
                    {
                        throw s.ErrorHere("PARSE002", "duplicate 'identity' section in entity '" + nameTok.Lexeme + "'");
                    }
                    identity = ParseIdentity(s);
                    break;
                case TokenKind.Relations:
                    if (relations is not null)
                    {
                        throw s.ErrorHere("PARSE002", "duplicate 'relations' section in entity '" + nameTok.Lexeme + "'");
                    }
                    relations = ParseRelations(s);
                    break;
                case TokenKind.Properties:
                    if (properties is not null)
                    {
                        throw s.ErrorHere("PARSE002", "duplicate 'properties' section in entity '" + nameTok.Lexeme + "'");
                    }
                    properties = ParseProperties(s);
                    break;
                case TokenKind.Lifecycle:
                    if (lifecycle is not null)
                    {
                        throw s.ErrorHere("PARSE002", "duplicate 'lifecycle' section in entity '" + nameTok.Lexeme + "'");
                    }
                    lifecycle = ParseLifecycle(s);
                    break;
                case TokenKind.Events:
                    if (events is not null)
                    {
                        throw s.ErrorHere("PARSE002", "duplicate 'events' section in entity '" + nameTok.Lexeme + "'");
                    }
                    events = ParseEvents(s);
                    break;
                case TokenKind.Commands:
                    if (commands is not null)
                    {
                        throw s.ErrorHere("PARSE002", "duplicate 'commands' section in entity '" + nameTok.Lexeme + "'");
                    }
                    commands = ParseCommands(s);
                    break;
                default:
                    throw s.ErrorHere("PARSE003",
                        "unexpected token '" + tok.Lexeme + "' in entity body; expected a section keyword or '}'");
            }
        }
        s.Expect(TokenKind.RBrace);

        if (identity is null)
        {
            throw new ParseException(new Diagnostic(
                DiagnosticSeverity.Error,
                "PARSE004",
                "entity '" + nameTok.Lexeme + "' is missing required 'identity' section",
                entityTok.Span));
        }

        return new EntityDecl(
            nameTok.Lexeme,
            version,
            deprecates,
            identity,
            relations ?? ImmutableArray<RelationDecl>.Empty,
            properties ?? ImmutableArray<PropertyDecl>.Empty,
            lifecycle ?? new LifecycleDecl(
                ImmutableArray<string>.Empty,
                ImmutableArray<TransitionDecl>.Empty,
                entityTok.Span),
            events ?? ImmutableArray<EventDecl>.Empty,
            commands ?? ImmutableArray<CommandDecl>.Empty,
            annotations,
            entityTok.Span);
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static ValueTypeDecl ParseValueType(ParserState s, ImmutableArray<AnnotationDecl> annotations)
    {
        s.EnterDepth();
        try
        {
        var typeTok = s.Expect(TokenKind.Type);
        var nameTok = s.Expect(TokenKind.Identifier);
        int version = 1;
        if (s.Peek().Kind == TokenKind.Version)
        {
            s.Consume();
            version = ParseInt(s);
        }
        s.Expect(TokenKind.LBrace);
        var fields = ImmutableArray.CreateBuilder<FieldDecl>();
        while (s.Peek().Kind != TokenKind.RBrace)
        {
            fields.Add(ParseField(s));
        }
        s.Expect(TokenKind.RBrace);
        return new ValueTypeDecl(nameTok.Lexeme, version, fields.ToImmutable(), annotations, typeTok.Span);
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static EnumDecl ParseEnum(ParserState s, ImmutableArray<AnnotationDecl> annotations)
    {
        s.EnterDepth();
        try
        {
        var enumTok = s.Expect(TokenKind.Enum);
        var nameTok = s.Expect(TokenKind.Identifier);
        int version = 1;
        if (s.Peek().Kind == TokenKind.Version)
        {
            s.Consume();
            version = ParseInt(s);
        }
        s.Expect(TokenKind.LBrace);
        var variants = ImmutableArray.CreateBuilder<string>();
        if (s.Peek().Kind != TokenKind.RBrace)
        {
            variants.Add(s.Expect(TokenKind.Identifier).Lexeme);
            while (s.Peek().Kind == TokenKind.Comma)
            {
                s.Consume();
                if (s.Peek().Kind == TokenKind.RBrace)
                {
                    // trailing comma
                    break;
                }
                variants.Add(s.Expect(TokenKind.Identifier).Lexeme);
            }
        }
        s.Expect(TokenKind.RBrace);
        return new EnumDecl(nameTok.Lexeme, version, variants.ToImmutable(), annotations, enumTok.Span);
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static IdentityDecl ParseIdentity(ParserState s)
    {
        s.EnterDepth();
        try
        {
        var idTok = s.Expect(TokenKind.Identity);
        var nameTok = s.Expect(TokenKind.Identifier);
        s.Expect(TokenKind.Colon);
        var type = ParseTypeRef(s);
        s.Expect(TokenKind.Semicolon);
        return new IdentityDecl(nameTok.Lexeme, type, idTok.Span);
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static ImmutableArray<RelationDecl> ParseRelations(ParserState s)
    {
        s.EnterDepth();
        try
        {
        s.Expect(TokenKind.Relations);
        s.Expect(TokenKind.LBrace);
        var b = ImmutableArray.CreateBuilder<RelationDecl>();
        while (s.Peek().Kind != TokenKind.RBrace)
        {
            var leading = ParseAnnotations(s);
            var nameTok = s.Expect(TokenKind.Identifier);
            s.Expect(TokenKind.Colon);
            var targetTok = s.Expect(TokenKind.Identifier);
            // FR-100 / T106: version suffix is not permitted on relation targets.
            RefuseVersionSuffix(s, "version suffix is not permitted on relation targets");
            bool optional = false;
            if (s.Peek().Kind == TokenKind.Question)
            {
                s.Consume();
                optional = true;
            }
            // Disallow [] suffix on relation targets per FR-022 — emit a parse-level
            // error so users get an immediate fix rather than a later VAL diagnostic.
            if (s.Peek().Kind == TokenKind.LBracket)
            {
                throw s.ErrorHere("PARSE005",
                    "relation target '" + targetTok.Lexeme + "' must not use '[]'; use 'cardinality many' instead");
            }
            s.Expect(TokenKind.Cardinality);
            var cardTok = s.Expect(TokenKind.Identifier);
            Cardinality cardinality = cardTok.Lexeme switch
            {
                "one" => Cardinality.One,
                "many" => Cardinality.Many,
                _ => throw new ParseException(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "PARSE006",
                    "cardinality must be 'one' or 'many', not '" + cardTok.Lexeme + "'",
                    cardTok.Span))
            };
            string? semantic = null;
            if (s.Peek().Kind == TokenKind.Semantic)
            {
                s.Consume();
                semantic = s.Expect(TokenKind.Identifier).Lexeme;
            }
            s.Expect(TokenKind.Semicolon);
            b.Add(new RelationDecl(
                nameTok.Lexeme,
                targetTok.Lexeme,
                optional,
                cardinality,
                semantic,
                leading,
                nameTok.Span));
        }
        s.Expect(TokenKind.RBrace);
        return b.ToImmutable();
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static ImmutableArray<PropertyDecl> ParseProperties(ParserState s)
    {
        s.EnterDepth();
        try
        {
        s.Expect(TokenKind.Properties);
        s.Expect(TokenKind.LBrace);
        var b = ImmutableArray.CreateBuilder<PropertyDecl>();
        while (s.Peek().Kind != TokenKind.RBrace)
        {
            var nameTok = s.Expect(TokenKind.Identifier);
            s.Expect(TokenKind.Colon);
            var type = ParseTypeRef(s);
            var trailing = ParseAnnotations(s);
            s.Expect(TokenKind.Semicolon);
            b.Add(new PropertyDecl(nameTok.Lexeme, type, trailing, nameTok.Span));
        }
        s.Expect(TokenKind.RBrace);
        return b.ToImmutable();
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static LifecycleDecl ParseLifecycle(ParserState s)
    {
        s.EnterDepth();
        try
        {
        var lcTok = s.Expect(TokenKind.Lifecycle);
        s.Expect(TokenKind.LBrace);

        ImmutableArray<string>? states = null;
        ImmutableArray<TransitionDecl>? transitions = null;

        while (s.Peek().Kind != TokenKind.RBrace)
        {
            var tok = s.Peek();
            if (tok.Kind == TokenKind.States)
            {
                s.Consume();
                s.Expect(TokenKind.LBrace);
                var sb = ImmutableArray.CreateBuilder<string>();
                if (s.Peek().Kind != TokenKind.Semicolon && s.Peek().Kind != TokenKind.RBrace)
                {
                    sb.Add(s.Expect(TokenKind.Identifier).Lexeme);
                    while (s.Peek().Kind == TokenKind.Comma)
                    {
                        s.Consume();
                        if (s.Peek().Kind == TokenKind.Semicolon || s.Peek().Kind == TokenKind.RBrace)
                        {
                            // trailing comma
                            break;
                        }
                        sb.Add(s.Expect(TokenKind.Identifier).Lexeme);
                    }
                }
                // states block may be terminated by ';' (per spec) or directly by '}'.
                if (s.Peek().Kind == TokenKind.Semicolon)
                {
                    s.Consume();
                }
                s.Expect(TokenKind.RBrace);
                states = sb.ToImmutable();
            }
            else if (tok.Kind == TokenKind.Transitions)
            {
                s.Consume();
                s.Expect(TokenKind.LBrace);
                var tb = ImmutableArray.CreateBuilder<TransitionDecl>();
                while (s.Peek().Kind != TokenKind.RBrace)
                {
                    var fromTok = s.Expect(TokenKind.Identifier);
                    s.Expect(TokenKind.Arrow);
                    var toTok = s.Expect(TokenKind.Identifier);
                    s.Expect(TokenKind.On);
                    var evtTok = s.Expect(TokenKind.Identifier);
                    s.Expect(TokenKind.Semicolon);
                    tb.Add(new TransitionDecl(fromTok.Lexeme, toTok.Lexeme, evtTok.Lexeme, fromTok.Span));
                }
                s.Expect(TokenKind.RBrace);
                transitions = tb.ToImmutable();
            }
            else
            {
                throw s.ErrorHere("PARSE007",
                    "expected 'states' or 'transitions' in lifecycle body, got '" + tok.Lexeme + "'");
            }
        }
        s.Expect(TokenKind.RBrace);
        return new LifecycleDecl(
            states ?? ImmutableArray<string>.Empty,
            transitions ?? ImmutableArray<TransitionDecl>.Empty,
            lcTok.Span);
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static ImmutableArray<EventDecl> ParseEvents(ParserState s)
    {
        s.EnterDepth();
        try
        {
        s.Expect(TokenKind.Events);
        s.Expect(TokenKind.LBrace);
        var b = ImmutableArray.CreateBuilder<EventDecl>();
        while (s.Peek().Kind != TokenKind.RBrace)
        {
            var nameTok = s.Expect(TokenKind.Identifier);
            s.Expect(TokenKind.LBrace);
            var payload = ImmutableArray.CreateBuilder<FieldDecl>();
            while (s.Peek().Kind != TokenKind.RBrace)
            {
                payload.Add(ParseField(s));
            }
            s.Expect(TokenKind.RBrace);
            s.Expect(TokenKind.Semicolon);
            b.Add(new EventDecl(nameTok.Lexeme, payload.ToImmutable(), ImmutableArray<AnnotationDecl>.Empty, nameTok.Span));
        }
        s.Expect(TokenKind.RBrace);
        return b.ToImmutable();
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static ImmutableArray<CommandDecl> ParseCommands(ParserState s)
    {
        s.EnterDepth();
        try
        {
        s.Expect(TokenKind.Commands);
        s.Expect(TokenKind.LBrace);
        var b = ImmutableArray.CreateBuilder<CommandDecl>();
        while (s.Peek().Kind != TokenKind.RBrace)
        {
            var nameTok = s.Expect(TokenKind.Identifier);
            s.Expect(TokenKind.LParen);
            var args = ImmutableArray.CreateBuilder<FieldDecl>();
            if (s.Peek().Kind != TokenKind.RParen)
            {
                args.Add(ParseFieldNoSemicolon(s));
                while (s.Peek().Kind == TokenKind.Comma)
                {
                    s.Consume();
                    args.Add(ParseFieldNoSemicolon(s));
                }
            }
            s.Expect(TokenKind.RParen);
            s.Expect(TokenKind.Returns);
            var returnsTok = s.Expect(TokenKind.Identifier);
            // FR-100 / T106a: version suffix is not permitted on command return types
            // (CommandDecl.ReturnsType is a bare string in the v1 AST).
            RefuseVersionSuffix(s, "version suffix is not permitted on command return types");
            s.Expect(TokenKind.With);
            s.Expect(TokenKind.SideEffect);
            var evtTok = s.Expect(TokenKind.Identifier);
            s.Expect(TokenKind.Semicolon);
            b.Add(new CommandDecl(
                nameTok.Lexeme,
                args.ToImmutable(),
                returnsTok.Lexeme,
                evtTok.Lexeme,
                ImmutableArray<AnnotationDecl>.Empty,
                nameTok.Span));
        }
        s.Expect(TokenKind.RBrace);
        return b.ToImmutable();
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static FieldDecl ParseField(ParserState s)
    {
        var nameTok = s.Expect(TokenKind.Identifier);
        s.Expect(TokenKind.Colon);
        var type = ParseTypeRef(s);
        s.Expect(TokenKind.Semicolon);
        return new FieldDecl(nameTok.Lexeme, type, nameTok.Span);
    }

    private static FieldDecl ParseFieldNoSemicolon(ParserState s)
    {
        var nameTok = s.Expect(TokenKind.Identifier);
        s.Expect(TokenKind.Colon);
        var type = ParseTypeRef(s);
        return new FieldDecl(nameTok.Lexeme, type, nameTok.Span);
    }

    private static TypeRef ParseTypeRef(ParserState s)
    {
        var nameTok = s.Expect(TokenKind.Identifier);

        // FR-100 / FR-101: optional '@N' version suffix is parsed BEFORE the '?'/'[]'
        // modifiers. Malformed suffixes emit PARSE020 at the '@' token and recover by
        // treating the suffix as absent so a single bad suffix does not cascade.
        //
        // Disambiguation: an '@' followed by an Identifier is an annotation, not a
        // version suffix (e.g. `name: String @csharp(...)` in a properties block).
        // The annotation parser consumes those at the next stage. An '@' followed by
        // anything else (IntegerLiteral, punctuation, EOF) is treated as a version
        // suffix attempt so malformed cases like `Money@`, `Money@ 2`, `Money@01`
        // still surface PARSE020.
        int? version = null;
        if (s.Peek().Kind == TokenKind.At && s.PeekAt(1).Kind != TokenKind.Identifier)
        {
            var atTok = s.Consume();
            if (!TryReadVersionSuffix(s, atTok, out version, out var diag))
            {
                s.Diagnostics.Add(diag!);
                version = null;
            }
        }

        // Parse [] and ? in either order; the spec (FR-011) makes the order significant
        // for emission of "String[]?" vs "String?[]", so capture exactly what was written.
        bool isOptional = false;
        bool isArray = false;
        if (s.Peek().Kind == TokenKind.Question)
        {
            s.Consume();
            isOptional = true;
            if (s.Peek().Kind == TokenKind.LBracket)
            {
                s.Consume();
                s.Expect(TokenKind.RBracket);
                isArray = true;
            }
        }
        else if (s.Peek().Kind == TokenKind.LBracket)
        {
            s.Consume();
            s.Expect(TokenKind.RBracket);
            isArray = true;
            if (s.Peek().Kind == TokenKind.Question)
            {
                s.Consume();
                isOptional = true;
            }
        }

        PrimitiveKind? prim = nameTok.Lexeme switch
        {
            "String" => PrimitiveKind.String,
            "Int" => PrimitiveKind.Int,
            "Long" => PrimitiveKind.Long,
            "Decimal" => PrimitiveKind.Decimal,
            "Boolean" => PrimitiveKind.Boolean,
            "Date" => PrimitiveKind.Date,
            "DateTime" => PrimitiveKind.DateTime,
            "UUID" => PrimitiveKind.Uuid,
            _ => null
        };
        if (prim is { } pk)
        {
            // FR-100 / T105: version suffix is not permitted on primitive types.
            if (version is not null)
            {
                s.Diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Parse020,
                    "version suffix is not permitted on primitive types",
                    nameTok.Span));
            }
            return new PrimitiveTypeRef(pk, isOptional, isArray, nameTok.Span);
        }
        return new NamedTypeRef(nameTok.Lexeme, isOptional, isArray, nameTok.Span, version);
    }

    /// <summary>
    /// Reads the integer literal of an <c>@N</c> version suffix following the '@'
    /// token <paramref name="atTok"/>. Returns <c>true</c> on success with the
    /// parsed positive integer in <paramref name="version"/>; returns <c>false</c>
    /// and a PARSE020 diagnostic on malformed input (FR-101).
    /// </summary>
    private static bool TryReadVersionSuffix(
        ParserState s,
        Token atTok,
        out int? version,
        out Diagnostic? diag)
    {
        version = null;
        var peek = s.Peek();
        // Must be an integer literal immediately adjacent to '@' (no intervening
        // whitespace). The lexer preserves column numbers; '@' has Length 1 so the
        // next token must start at atTok.Column + 1.
        if (peek.Kind != TokenKind.IntegerLiteral)
        {
            diag = new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Parse020,
                "expected positive integer after '@'",
                atTok.Span);
            return false;
        }
        if (peek.Span.Column != atTok.Span.Column + 1)
        {
            diag = new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Parse020,
                "whitespace is not allowed between '@' and the version number",
                atTok.Span);
            // Do not consume the literal; let the caller continue.
            return false;
        }
        // Leading zero (e.g. "01") is rejected. NumberStyles.None forbids leading
        // '+'/'-' and leading whitespace automatically; the lexer never includes
        // those in the IntegerLiteral lexeme anyway, but a leading zero is a
        // syntactically valid integer to the lexer and must be rejected here.
        var lexeme = peek.Lexeme;
        if (lexeme.Length > 1 && lexeme[0] == '0')
        {
            s.Consume();
            diag = new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Parse020,
                "version suffix must not have a leading zero",
                atTok.Span);
            return false;
        }
        if (!int.TryParse(lexeme, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            s.Consume();
            diag = new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Parse020,
                "version suffix must be a positive integer",
                atTok.Span);
            return false;
        }
        s.Consume();
        version = n;
        diag = null;
        return true;
    }

    /// <summary>
    /// Refuses an <c>@N</c> suffix at a parse position that is not permitted by
    /// FR-100 (relation target, command <c>returns</c>). When the next token is
    /// <c>@</c>, emits PARSE020 with <paramref name="contextMessage"/> and
    /// consumes the malformed suffix so the rest of the production still parses.
    /// </summary>
    private static void RefuseVersionSuffix(ParserState s, string contextMessage)
    {
        if (s.Peek().Kind != TokenKind.At)
        {
            return;
        }
        var atTok = s.Consume();
        s.Diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            RuleIds.Parse020,
            contextMessage,
            atTok.Span));
        // Drain a directly-following integer literal so the suffix does not
        // pollute downstream parsing. Column-adjacency is the same check used
        // by TryReadVersionSuffix.
        var peek = s.Peek();
        if (peek.Kind == TokenKind.IntegerLiteral
            && peek.Span.Column == atTok.Span.Column + 1)
        {
            s.Consume();
        }
    }

    private static ImmutableArray<AnnotationDecl> ParseAnnotations(ParserState s)
    {
        s.EnterDepth();
        try
        {
        if (s.Peek().Kind != TokenKind.At)
        {
            return ImmutableArray<AnnotationDecl>.Empty;
        }
        var b = ImmutableArray.CreateBuilder<AnnotationDecl>();
        while (s.Peek().Kind == TokenKind.At)
        {
            var atTok = s.Consume();
            var firstId = s.Expect(TokenKind.Identifier);
            string ns = firstId.Lexeme;
            string name = string.Empty;
            // Optional dotted name: @ns.name(...). When absent, the namespace acts as
            // both namespace and name; rendered as @ns(...) on round-trip.
            if (s.Peek().Kind == TokenKind.Dot)
            {
                s.Consume();
                var nameTok = s.Expect(TokenKind.Identifier);
                name = nameTok.Lexeme;
            }
            var args = ImmutableSortedDictionary.CreateBuilder<string, AnnotationValue>(StringComparer.Ordinal);
            if (s.Peek().Kind == TokenKind.LParen)
            {
                s.Consume();
                if (s.Peek().Kind != TokenKind.RParen)
                {
                    ParseAnnotationArg(s, args);
                    while (s.Peek().Kind == TokenKind.Comma)
                    {
                        s.Consume();
                        ParseAnnotationArg(s, args);
                    }
                }
                s.Expect(TokenKind.RParen);
            }
            b.Add(new AnnotationDecl(ns, name, args.ToImmutable(), atTok.Span));
        }
        return b.ToImmutable();
        }
        finally
        {
            s.ExitDepth();
        }
    }

    private static void ParseAnnotationArg(
        ParserState s,
        ImmutableSortedDictionary<string, AnnotationValue>.Builder args)
    {
        var keyTok = s.Expect(TokenKind.Identifier);
        s.Expect(TokenKind.Colon);
        var value = ParseAnnotationValue(s);
        if (args.ContainsKey(keyTok.Lexeme))
        {
            throw new ParseException(new Diagnostic(
                DiagnosticSeverity.Error,
                "PARSE008",
                "duplicate annotation argument '" + keyTok.Lexeme + "'",
                keyTok.Span));
        }
        args[keyTok.Lexeme] = value;
    }

    private static AnnotationValue ParseAnnotationValue(ParserState s)
    {
        var tok = s.Peek();
        switch (tok.Kind)
        {
            case TokenKind.StringLiteral:
                s.Consume();
                return new AnnotationStringValue(tok.Lexeme);
            case TokenKind.IntegerLiteral:
                s.Consume();
                // Guard against overflow on annotation arguments. long.Parse would
                // throw OverflowException on inputs exceeding 9223372036854775807;
                // emit PARSE021 and recover with 0 so the rest of the source still
                // parses.
                if (!long.TryParse(tok.Lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                {
                    s.Diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        RuleIds.Parse021,
                        "annotation integer value must be in -9223372036854775808..9223372036854775807 range",
                        tok.Span));
                    iv = 0;
                }
                return new AnnotationIntValue(iv);
            case TokenKind.DecimalLiteral:
                s.Consume();
                return new AnnotationDecimalValue(decimal.Parse(tok.Lexeme, NumberStyles.Number, CultureInfo.InvariantCulture));
            case TokenKind.True:
                s.Consume();
                return new AnnotationBoolValue(true);
            case TokenKind.False:
                s.Consume();
                return new AnnotationBoolValue(false);
            case TokenKind.Identifier:
                s.Consume();
                return new AnnotationIdentValue(tok.Lexeme);
            default:
                throw s.ErrorHere("PARSE009",
                    "expected annotation value (string, integer, decimal, bool, or identifier)");
        }
    }

    private static int ParseInt(ParserState s)
    {
        var tok = s.Expect(TokenKind.IntegerLiteral);
        // FR-101 (and Phase 4 safety): an int.Parse on a multi-digit literal
        // that exceeds Int32.MaxValue throws OverflowException, which would
        // crash the compiler from user input. Emit PARSE020 and recover by
        // pinning the value to 1 so downstream production still parses and
        // a single bad number does not cascade into a parse cascade.
        if (!int.TryParse(tok.Lexeme, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
        {
            s.Diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Parse020,
                "version number must be a positive integer in 1..2147483647 range",
                tok.Span));
            return 1;
        }
        return n;
    }

    private sealed class ParserState
    {
        private readonly ImmutableArray<Token> _tokens;
        private int _pos;
        private int _depth;

        public ParserState(ImmutableArray<Token> tokens, string path, List<Diagnostic> diagnostics)
        {
            _tokens = tokens;
            Path = path;
            Diagnostics = diagnostics;
        }

        public string Path { get; }
        public List<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// Optional per-parse depth cap for tests. <c>null</c> means use the compile-time
        /// <see cref="RuleIds.MaxDepth"/> default. Production callers leave this unset.
        /// </summary>
        public int? MaxDepthOverride { get; init; }

        public Token Peek() => _tokens[_pos];

        /// <summary>
        /// Look ahead <paramref name="offset"/> tokens without consuming. Returns the
        /// final EndOfFile token when the offset runs off the end of the stream.
        /// </summary>
        public Token PeekAt(int offset)
        {
            int i = _pos + offset;
            if (i >= _tokens.Length) i = _tokens.Length - 1;
            return _tokens[i];
        }

        public Token Consume()
        {
            var t = _tokens[_pos];
            if (_pos < _tokens.Length - 1) _pos++;
            return t;
        }

        public Token Expect(TokenKind kind)
        {
            var t = _tokens[_pos];
            if (t.Kind != kind)
            {
                throw new ParseException(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "PARSE000",
                    "expected " + kind + " but got '" + t.Lexeme + "' (" + t.Kind + ")",
                    t.Span));
            }
            if (_pos < _tokens.Length - 1) _pos++;
            return t;
        }

        /// <summary>
        /// Increment the per-state recursion depth counter. Throws <see cref="ParseException"/>
        /// with rule id <c>PARSE010</c> once the depth exceeds <see cref="RuleIds.MaxDepth"/>.
        /// Callers MUST pair every successful <c>EnterDepth</c> with an <see cref="ExitDepth"/>
        /// in a <c>finally</c> block so error paths still unwind.
        /// </summary>
        public void EnterDepth()
        {
            _depth++;
            int cap = MaxDepthOverride ?? RuleIds.MaxDepth;
            if (_depth > cap)
            {
                var t = _tokens[_pos];
                throw new ParseException(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Parse010,
                    "maximum nesting depth (" + RuleIds.MaxDepth + ") exceeded",
                    t.Span));
            }
        }

        public void ExitDepth()
        {
            if (_depth > 0) _depth--;
        }

        public ParseException ErrorHere(string ruleId, string message)
        {
            var t = _tokens[_pos];
            return new ParseException(new Diagnostic(
                DiagnosticSeverity.Error,
                ruleId,
                message,
                t.Span));
        }
    }

    private sealed class ParseException : Exception
    {
        public ParseException(Diagnostic diag)
        {
            Diagnostic = diag;
        }

        public Diagnostic Diagnostic { get; }
    }
}
