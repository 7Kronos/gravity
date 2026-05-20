using System;
using System.Collections.Immutable;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// FR-440 / FR-441 — fold <c>@postgres(...)</c> annotations onto a property's
/// rendered shape. Returns a typed bundle of overrides plus appends diagnostics
/// for unknown keys, mismatched value types, and reserved-for-future keys.
/// </summary>
internal static class AnnotationFolder
{
    /// <summary>
    /// The folded attributes a property carries after consuming its
    /// <c>@postgres</c> annotations. All fields are independent; the
    /// column-rendering layer composes them in the right order.
    /// </summary>
    public sealed record FoldedAttributes(
        string? OverrideColumnName,
        bool MarkUnique,
        bool MarkIndexed,
        string? DefaultExpression);

    /// <summary>The empty / no-overrides bundle. Used as the safe fallback on diagnostic.</summary>
    public static readonly FoldedAttributes None =
        new(OverrideColumnName: null, MarkUnique: false, MarkIndexed: false, DefaultExpression: null);

    /// <summary>
    /// Walk <paramref name="annotations"/> looking for the <c>postgres</c>
    /// namespace and fold its keys per FR-441. Diagnostics (PG003 / PG004) are
    /// appended to <paramref name="diags"/>; the returned attributes are the
    /// best-effort projection of the successfully-folded keys.
    /// </summary>
    public static FoldedAttributes Fold(
        ImmutableArray<AnnotationDecl> annotations,
        string propertyName,
        string entityFqn,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        string? overrideColumn = null;
        bool markUnique = false;
        bool markIndexed = false;
        string? defaultExpr = null;

        foreach (var ann in annotations)
        {
            if (!string.Equals(ann.Namespace, "postgres", StringComparison.Ordinal)) continue;
            foreach (var kv in ann.Arguments)
            {
                switch (kv.Key)
                {
                    case "column":
                    {
                        if (kv.Value is AnnotationStringValue s)
                        {
                            if (!Identifier.IsValidPgIdentifier(s.Value))
                            {
                                diags.Add(new Diagnostic(
                                    DiagnosticSeverity.Error,
                                    PgRuleIds.Pg004,
                                    "property '" + propertyName + "' on '" + entityFqn
                                        + "' carries @postgres(column: '" + s.Value
                                        + "') which is not a valid PostgreSQL identifier ([a-z_][a-z0-9_]*, length 1..63)",
                                    ann.Span));
                            }
                            else
                            {
                                overrideColumn = s.Value;
                            }
                        }
                        else
                        {
                            diags.Add(WrongType(ann.Span, propertyName, entityFqn, "column", "string", kv.Value));
                        }
                        break;
                    }
                    case "unique":
                    {
                        if (kv.Value is AnnotationBoolValue b) markUnique = b.Value;
                        else diags.Add(WrongType(ann.Span, propertyName, entityFqn, "unique", "boolean", kv.Value));
                        break;
                    }
                    case "index":
                    {
                        if (kv.Value is AnnotationBoolValue b) markIndexed = b.Value;
                        else diags.Add(WrongType(ann.Span, propertyName, entityFqn, "index", "boolean", kv.Value));
                        break;
                    }
                    case "default":
                    {
                        if (kv.Value is AnnotationStringValue s) defaultExpr = s.Value;
                        else diags.Add(WrongType(ann.Span, propertyName, entityFqn, "default", "string", kv.Value));
                        break;
                    }
                    // Reserved-for-future, not consumed in v1.
                    case "precision":
                    case "scale":
                    case "max_length":
                    case "storage":
                    case "index_method":
                    case "partial_where":
                    {
                        diags.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            PgRuleIds.Pg003,
                            "property '" + propertyName + "' on '" + entityFqn
                                + "' uses @postgres(" + kv.Key
                                + "); this key is reserved for future use and is not consumed in v1",
                            ann.Span));
                        break;
                    }
                    default:
                    {
                        diags.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            PgRuleIds.Pg003,
                            "property '" + propertyName + "' on '" + entityFqn
                                + "' carries unknown @postgres key '" + kv.Key
                                + "'; the postgres namespace claims {column, index, unique, default}",
                            ann.Span));
                        break;
                    }
                }
            }
        }

        return new FoldedAttributes(overrideColumn, markUnique, markIndexed, defaultExpr);
    }

    private static Diagnostic WrongType(SourceSpan span, string propertyName, string entityFqn, string key, string expected, AnnotationValue actual)
    {
        string actualKind = actual switch
        {
            AnnotationStringValue  => "string",
            AnnotationIntValue     => "integer",
            AnnotationDecimalValue => "decimal",
            AnnotationBoolValue    => "boolean",
            AnnotationIdentValue   => "identifier",
            _                      => "unknown",
        };
        return new Diagnostic(
            DiagnosticSeverity.Error,
            PgRuleIds.Pg003,
            "property '" + propertyName + "' on '" + entityFqn
                + "' carries @postgres(" + key + ") with a " + actualKind
                + " value; expected " + expected,
            span);
    }
}
