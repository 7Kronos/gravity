using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Dsl.Emitter.JsonSchema;

/// <summary>
/// Central determinism hinge. Accepts a mutable <see cref="JsonNode"/> tree
/// populated by the renderers in canonical declaration order and emits UTF-8
/// (no BOM) bytes with <c>\n</c> line endings, 2-space indent. Enforces the
/// fixed canonical top-level key order from FR-351; sorts <c>required</c>
/// arrays (FR-353) and <c>definitions</c> entries (FR-355) at write time;
/// preserves <c>properties</c> declaration order (FR-352) and <c>enum</c>
/// declaration order (FR-354).
/// </summary>
internal static class SortedKeyJsonWriter
{
    /// <summary>
    /// Fixed canonical top-level key order applied to every object schema
    /// (FR-351). Keys not in this list (annotation-folded <c>description</c>,
    /// <c>format</c>, <c>pattern</c>, <c>minLength</c>, <c>maxLength</c>,
    /// <c>minimum</c>, <c>maximum</c>, <c>multipleOf</c>, <c>examples</c>,
    /// plus the relation-encoded <c>items</c>, <c>uniqueItems</c>) appear after
    /// the canonical list, sorted ordinally.
    /// </summary>
    private static readonly string[] CanonicalKeyOrder = new[]
    {
        "$schema",
        "title",
        "x-gravity-version",
        "type",
        "properties",
        "required",
        "additionalProperties",
        "enum",
        "definitions",
    };

    /// <summary>
    /// Serialise <paramref name="root"/> to a UTF-8 (no BOM) string with LF
    /// line endings, 2-space indent, canonical key order, sorted
    /// <c>required</c>/<c>definitions</c>, and declaration order preserved on
    /// <c>properties</c>/<c>enum</c>.
    /// </summary>
    public static string Serialize(JsonNode root)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        using var ms = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            // NewLine is honoured by .NET 9 Utf8JsonWriter when Indented = true.
            NewLine = "\n",
        };
        using (var writer = new Utf8JsonWriter(ms, options))
        {
            WriteNode(writer, root);
        }
        // BufferedEmitterOutput writes UTF-8 no BOM with LF line endings by
        // construction; we additionally normalise any CR injection here as
        // defence in depth (FR-350).
        var text = Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n").Replace("\r", "\n");
        // Append trailing LF so files end with a newline, matching the
        // conventional UNIX text-file layout the rest of the emitter set uses.
        if (text.Length == 0 || text[text.Length - 1] != '\n')
        {
            text += "\n";
        }
        return text;
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                WriteObject(writer, obj);
                break;
            case JsonArray arr:
                writer.WriteStartArray();
                foreach (var elem in arr)
                {
                    WriteNode(writer, elem);
                }
                writer.WriteEndArray();
                break;
            case JsonValue val:
                WriteValue(writer, val);
                break;
            default:
                throw new InvalidOperationException("unsupported JsonNode kind: " + node.GetType().Name);
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonObject obj)
    {
        writer.WriteStartObject();

        // $ref short-circuit: a Draft-07 reference object's siblings are
        // conventionally ignored, but for tidy output we still emit them
        // sorted ordinally after the $ref key.
        if (obj.ContainsKey("$ref"))
        {
            writer.WritePropertyName("$ref");
            WriteNode(writer, obj["$ref"]);
            foreach (var key in obj.Select(kv => kv.Key)
                                   .Where(k => !string.Equals(k, "$ref", StringComparison.Ordinal))
                                   .OrderBy(k => k, StringComparer.Ordinal))
            {
                WriteKeyAndValue(writer, key, obj[key]);
            }
            writer.WriteEndObject();
            return;
        }

        // Canonical-first ordering.
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in obj)
        {
            present.Add(kv.Key);
        }
        foreach (var canonicalKey in CanonicalKeyOrder)
        {
            if (!present.Contains(canonicalKey)) continue;
            switch (canonicalKey)
            {
                case "required":
                    WriteSortedStringArray(writer, canonicalKey, obj[canonicalKey]);
                    break;
                case "definitions":
                    WriteSortedKeyObject(writer, canonicalKey, obj[canonicalKey]);
                    break;
                case "properties":
                    // FR-352 — property map entries appear in DSL declaration
                    // order (insertion order on the JsonObject), NOT sorted.
                    WriteInsertionOrderObject(writer, canonicalKey, obj[canonicalKey]);
                    break;
                default:
                    WriteKeyAndValue(writer, canonicalKey, obj[canonicalKey]);
                    break;
            }
        }
        // Overflow: keys not in CanonicalKeyOrder, sorted ordinally.
        foreach (var key in obj.Select(kv => kv.Key)
                               .Where(k => !IsCanonical(k))
                               .OrderBy(k => k, StringComparer.Ordinal))
        {
            WriteKeyAndValue(writer, key, obj[key]);
        }
        writer.WriteEndObject();
    }

    private static bool IsCanonical(string key)
    {
        for (int i = 0; i < CanonicalKeyOrder.Length; i++)
        {
            if (string.Equals(CanonicalKeyOrder[i], key, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static void WriteKeyAndValue(Utf8JsonWriter writer, string key, JsonNode? value)
    {
        writer.WritePropertyName(key);
        WriteNode(writer, value);
    }

    /// <summary>FR-353 — write a string array in sorted ordinal order.</summary>
    private static void WriteSortedStringArray(Utf8JsonWriter writer, string key, JsonNode? value)
    {
        writer.WritePropertyName(key);
        if (value is not JsonArray arr)
        {
            // Defensive: emit verbatim so a renderer bug surfaces obviously.
            WriteNode(writer, value);
            return;
        }
        var strings = new List<string>(arr.Count);
        foreach (var elem in arr)
        {
            if (elem is JsonValue jv && jv.TryGetValue<string>(out var s))
            {
                strings.Add(s);
            }
            else
            {
                // Non-string element — fall back to verbatim write to surface bugs.
                WriteNode(writer, arr);
                return;
            }
        }
        strings.Sort(StringComparer.Ordinal);
        writer.WriteStartArray();
        foreach (var s in strings)
        {
            writer.WriteStringValue(s);
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// FR-352 — write an object whose keys appear in insertion order
    /// (i.e. DSL declaration order, since renderers walk AST in source order).
    /// Each value is recursively serialised through <see cref="WriteNode"/> so
    /// nested schemas still follow canonical key ordering.
    /// </summary>
    private static void WriteInsertionOrderObject(Utf8JsonWriter writer, string key, JsonNode? value)
    {
        writer.WritePropertyName(key);
        if (value is not JsonObject obj)
        {
            WriteNode(writer, value);
            return;
        }
        writer.WriteStartObject();
        foreach (var kv in obj)
        {
            writer.WritePropertyName(kv.Key);
            WriteNode(writer, kv.Value);
        }
        writer.WriteEndObject();
    }

    /// <summary>FR-355 — write an object whose keys are sorted ordinally.</summary>
    private static void WriteSortedKeyObject(Utf8JsonWriter writer, string key, JsonNode? value)
    {
        writer.WritePropertyName(key);
        if (value is not JsonObject obj)
        {
            WriteNode(writer, value);
            return;
        }
        writer.WriteStartObject();
        foreach (var k in obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal))
        {
            writer.WritePropertyName(k);
            WriteNode(writer, obj[k]);
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonValue val)
    {
        // Try each scalar kind explicitly so we control formatting (e.g. integers
        // stay integers, no scientific notation on doubles, decimals round-trip).
        if (val.TryGetValue<bool>(out var b))
        {
            writer.WriteBooleanValue(b);
            return;
        }
        if (val.TryGetValue<int>(out var i))
        {
            writer.WriteNumberValue(i);
            return;
        }
        if (val.TryGetValue<long>(out var l))
        {
            writer.WriteNumberValue(l);
            return;
        }
        if (val.TryGetValue<decimal>(out var dec))
        {
            writer.WriteNumberValue(dec);
            return;
        }
        if (val.TryGetValue<double>(out var d))
        {
            writer.WriteNumberValue(d);
            return;
        }
        if (val.TryGetValue<string>(out var s))
        {
            writer.WriteStringValue(s);
            return;
        }
        // Fallback: write via underlying JsonElement to preserve whatever
        // representation System.Text.Json chose.
        val.WriteTo(writer);
    }
}
