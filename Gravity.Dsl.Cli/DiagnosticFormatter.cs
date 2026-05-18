using System.Globalization;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Cli;

/// <summary>
/// Renders a <see cref="Diagnostic"/> in the canonical
/// <c>path:line:col: severity ruleId: message</c> format used by the gravc CLI
/// and asserted by AC-7.
/// </summary>
internal static class DiagnosticFormatter
{
    /// <summary>Format a single diagnostic for stderr/stdout consumption.</summary>
    public static string Format(Diagnostic diagnostic)
    {
        var sev = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "info"
        };
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:{2}: {3} {4}: {5}",
            diagnostic.Span.Path,
            diagnostic.Span.Line,
            diagnostic.Span.Column,
            sev,
            diagnostic.RuleId,
            diagnostic.Message);
    }
}
