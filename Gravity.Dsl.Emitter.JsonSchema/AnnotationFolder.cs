using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.JsonSchema;

/// <summary>
/// Folds <c>@json_schema(...)</c> annotations onto a property's schema
/// fragment. The claimed keyword subset (FR-340) is closed; per-key value-type
/// contracts (FR-341) are enforced here. Mismatches produce <c>JS001</c>;
/// unknown format strings produce <c>JS004</c> (Warning).
/// </summary>
internal static class AnnotationFolder
{
    /// <summary>FR-340 closed claimed-key set for <c>@json_schema</c>.</summary>
    private static readonly HashSet<string> ClaimedKeys = new(StringComparer.Ordinal)
    {
        "format", "pattern", "description", "examples",
        "minLength", "maxLength", "minimum", "maximum", "multipleOf",
    };

    /// <summary>
    /// Keys that constrain the per-item value when the outer fragment is an
    /// array wrapper (<c>{ "type": "array", "items": ... }</c>). Draft-07
    /// has no meaning for these keys at the array level; they belong on
    /// <c>items</c>. <c>description</c> and <c>examples</c> are intentionally
    /// excluded — they remain meaningful at the array level.
    /// </summary>
    private static readonly HashSet<string> ItemLevelKeys = new(StringComparer.Ordinal)
    {
        "format", "pattern", "minLength", "maxLength", "minimum", "maximum", "multipleOf",
    };

    /// <summary>
    /// FR-341 known format set. Unknown format values pass through verbatim
    /// but emit <c>JS004</c> at Warning severity (FR-341 / FR-344 split:
    /// consumers using newer-draft formats are not blocked).
    /// </summary>
    private static readonly HashSet<string> KnownFormats = new(StringComparer.Ordinal)
    {
        "email", "uri", "uuid", "date", "date-time", "time",
        "hostname", "ipv4", "ipv6", "regex", "decimal",
    };

    /// <summary>
    /// Fold every <c>@json_schema(...)</c> annotation on <paramref name="annotations"/>
    /// onto <paramref name="fragment"/> in place. Diagnostics are appended to
    /// <paramref name="diags"/>.
    /// </summary>
    public static void FoldOntoProperty(
        JsonObject fragment,
        ImmutableArray<AnnotationDecl> annotations,
        string propertyName,
        string ownerFqn,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        if (fragment is null) throw new ArgumentNullException(nameof(fragment));

        // FR-332 array wrapper routing: when the outer fragment is an array
        // wrapper ({ "type": "array", "items": ... }), item-level constraint
        // keywords (pattern, minLength, etc.) are meaningless at the array
        // level in Draft-07. Route them to the items schema; keep array-level
        // metadata (description, examples) on the wrapper itself.
        JsonObject targetForItemLevel = fragment;
        bool isArrayWrapper = false;
        if (fragment.TryGetPropertyValue("type", out var typeNode)
            && typeNode is JsonValue tv
            && tv.TryGetValue<string>(out var typeStr)
            && string.Equals(typeStr, "array", StringComparison.Ordinal)
            && fragment.TryGetPropertyValue("items", out var itemsNode)
            && itemsNode is JsonObject itemsObj)
        {
            isArrayWrapper = true;
            targetForItemLevel = itemsObj;
        }

        foreach (var ann in annotations)
        {
            if (!string.Equals(ann.Namespace, "json_schema", StringComparison.Ordinal)) continue;
            foreach (var kv in ann.Arguments)
            {
                string key = kv.Key;
                AnnotationValue value = kv.Value;
                if (!ClaimedKeys.Contains(key))
                {
                    diags.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        JsonRuleIds.Js001,
                        "property '" + propertyName + "' on '" + ownerFqn + "': unknown json_schema key '" + key
                            + "'; the json_schema namespace claims {format, pattern, description, examples, minLength, maxLength, minimum, maximum, multipleOf}",
                        ann.Span));
                    continue;
                }
                JsonObject target = isArrayWrapper && ItemLevelKeys.Contains(key)
                    ? targetForItemLevel
                    : fragment;
                switch (key)
                {
                    case "format":
                        if (!TryString(value, out var fmt))
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key, "string", value);
                            break;
                        }
                        target["format"] = fmt;
                        if (!KnownFormats.Contains(fmt))
                        {
                            diags.Add(new Diagnostic(
                                DiagnosticSeverity.Warning,
                                JsonRuleIds.Js004,
                                "property '" + propertyName + "' on '" + ownerFqn
                                    + "': @json_schema(format: \"" + fmt + "\") is not in the emitter's known format set",
                                ann.Span));
                        }
                        break;

                    case "pattern":
                    case "description":
                        if (!TryString(value, out var s))
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key, "string", value);
                            break;
                        }
                        target[key] = s;
                        break;

                    case "examples":
                        if (!TryString(value, out var ex))
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key, "string", value);
                            break;
                        }
                        // FR-341: wrap single string into one-element array.
                        target["examples"] = new JsonArray(JsonValue.Create(ex));
                        break;

                    case "minLength":
                    case "maxLength":
                        if (!TryInt(value, out var len) || len < 0)
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key, "non-negative integer", value);
                            break;
                        }
                        target[key] = len;
                        break;

                    case "minimum":
                    case "maximum":
                        if (!TryNumeric(value, out var min, out var minDec, out var minIsDec))
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key, "integer or decimal", value);
                            break;
                        }
                        target[key] = minIsDec ? JsonValue.Create(minDec) : JsonValue.Create(min);
                        break;

                    case "multipleOf":
                        if (!TryNumeric(value, out var mult, out var multDec, out var multIsDec))
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key, "integer or decimal", value);
                            break;
                        }
                        bool isZero = multIsDec ? multDec == 0m : mult == 0L;
                        if (isZero)
                        {
                            AddTypeMismatch(diags, ann, propertyName, ownerFqn, key,
                                "non-zero number (Draft-07 forbids multipleOf: 0)", value);
                            break;
                        }
                        target[key] = multIsDec ? JsonValue.Create(multDec) : JsonValue.Create(mult);
                        break;
                }
            }
        }
    }

    private static void AddTypeMismatch(
        ImmutableArray<Diagnostic>.Builder diags,
        AnnotationDecl ann,
        string propertyName,
        string ownerFqn,
        string key,
        string expected,
        AnnotationValue actual)
    {
        diags.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            JsonRuleIds.Js001,
            "property '" + propertyName + "' on '" + ownerFqn + "': annotation @json_schema(" + key
                + ") expects " + expected + ", got " + DescribeValue(actual),
            ann.Span));
    }

    private static string DescribeValue(AnnotationValue value) => value switch
    {
        AnnotationStringValue s => "string literal \"" + s.Value + "\"",
        AnnotationIntValue i => "integer literal " + i.Value.ToString(CultureInfo.InvariantCulture),
        AnnotationDecimalValue d => "decimal literal " + d.Value.ToString(CultureInfo.InvariantCulture),
        AnnotationBoolValue b => "boolean literal " + (b.Value ? "true" : "false"),
        AnnotationIdentValue i => "identifier '" + i.Value + "'",
        _ => "value of unknown kind",
    };

    private static bool TryString(AnnotationValue v, out string result)
    {
        if (v is AnnotationStringValue s) { result = s.Value; return true; }
        result = string.Empty;
        return false;
    }

    private static bool TryInt(AnnotationValue v, out long result)
    {
        if (v is AnnotationIntValue i) { result = i.Value; return true; }
        result = 0;
        return false;
    }

    /// <summary>
    /// Numeric values may be integer or decimal. Returns the integer in
    /// <paramref name="asInt"/> when the value is an integer literal; the
    /// decimal in <paramref name="asDec"/> when it is a decimal literal.
    /// <paramref name="isDecimal"/> is true for decimal literals.
    /// </summary>
    private static bool TryNumeric(AnnotationValue v, out long asInt, out decimal asDec, out bool isDecimal)
    {
        if (v is AnnotationIntValue i)
        {
            asInt = i.Value;
            asDec = 0m;
            isDecimal = false;
            return true;
        }
        if (v is AnnotationDecimalValue d)
        {
            asInt = 0;
            asDec = d.Value;
            isDecimal = true;
            return true;
        }
        asInt = 0;
        asDec = 0m;
        isDecimal = false;
        return false;
    }
}
