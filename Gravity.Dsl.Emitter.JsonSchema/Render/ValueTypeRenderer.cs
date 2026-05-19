using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// FR-316 — renders a namespace-scope value type as a stand-alone Draft-07
/// schema document. Fields appear in DSL declaration order under
/// <c>properties</c>; <c>required</c> is sorted ordinally; cross-references
/// to other value types or enums go through <c>$ref</c> to a sibling file
/// (FR-318).
/// </summary>
internal static class ValueTypeRenderer
{
    public static string Render(
        ValueTypeDecl vt,
        ResolvedModel model,
        DeclKey key,
        string? referrerNamespace,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var f in vt.Fields)
        {
            var frag = PropertyRenderer.RenderField(
                f.Name, f.Type, referrerNamespace, model, multiVersionFqns, key.Fqn, diags);
            properties[f.Name] = frag;
            bool optional = f.Type switch
            {
                PrimitiveTypeRef p => p.IsOptional,
                NamedTypeRef n => n.IsOptional,
                _ => false,
            };
            if (!optional)
            {
                required.Add(JsonValue.Create(f.Name));
            }
        }
        var doc = new JsonObject
        {
            ["$schema"] = JsonSchemaEmitter.Draft07Uri,
            ["title"] = key.Fqn,
            ["x-gravity-version"] = key.Version,
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
        return SortedKeyJsonWriter.Serialize(doc);
    }
}
