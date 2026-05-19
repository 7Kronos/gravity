using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// FR-311 — renders one event-payload <c>definitions</c> entry inside the
/// entity bundle. Shape: <c>{ "title": "&lt;Fqn&gt;.&lt;EventName&gt;", "type":
/// "object", "properties": {...}, "required": [...sorted ordinal],
/// "additionalProperties": false }</c>.
/// </summary>
internal static class EventPayloadRenderer
{
    public static JsonObject Render(
        EventDecl ev,
        string entityFqn,
        string? referrerNamespace,
        ResolvedModel model,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        // DSL declaration order is preserved for `properties` per FR-352.
        foreach (var f in ev.Payload)
        {
            var frag = PropertyRenderer.RenderField(
                f.Name, f.Type, referrerNamespace, model, multiVersionFqns, entityFqn + "." + ev.Name, diags);
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
        return new JsonObject
        {
            ["title"] = entityFqn + "." + ev.Name,
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }
}
