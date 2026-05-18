namespace Gravity.Dsl.Ast;

/// <summary>
/// Severity of a <see cref="Diagnostic"/>.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Build-stopping problem. The CLI exits non-zero on any error.</summary>
    Error,

    /// <summary>Recoverable problem. Build continues; users SHOULD investigate.</summary>
    Warning,

    /// <summary>Purely informational. Build continues unchanged.</summary>
    Info
}

/// <summary>
/// A diagnostic produced by any compiler stage (lexer, parser, resolver, validator,
/// emitter host, emitter). Carries the stable rule id, a human-readable message, and
/// a source span so the formatter can render <c>path:line:col: severity rule-id: message</c>.
/// </summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string RuleId,
    string Message,
    SourceSpan Span);
