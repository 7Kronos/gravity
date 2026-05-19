using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// FR-317 — renders a namespace-scope enum as a stand-alone Draft-07 string
/// schema. Variant order is DSL declaration order (FR-354 — asymmetric with
/// <c>required</c> which IS sorted; enums carry semantic ordering).
/// </summary>
internal static class EnumRenderer
{
    public static string Render(EnumDecl en, DeclKey key)
    {
        var variants = new JsonArray();
        foreach (var v in en.Variants)
        {
            variants.Add(JsonValue.Create(v));
        }
        var doc = new JsonObject
        {
            ["$schema"] = JsonSchemaEmitter.Draft07Uri,
            ["title"] = key.Fqn,
            ["x-gravity-version"] = key.Version,
            ["type"] = "string",
            ["enum"] = variants,
        };
        return SortedKeyJsonWriter.Serialize(doc);
    }
}
