using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// FR-315 — renders an entity's lifecycle state set as the body of the
/// <c>definitions/&lt;EntityName&gt;State</c> entry inside the entity bundle.
/// Variants follow DSL declaration order (FR-354 — the first state is the
/// implicit initial state per FR-033).
/// </summary>
internal static class LifecycleStateRenderer
{
    /// <summary>
    /// Render <paramref name="lifecycle"/> as a Draft-07 string enum schema.
    /// Returns <c>{ "type": "string", "enum": [ ... states in DSL order ] }</c>.
    /// </summary>
    public static JsonObject Render(LifecycleDecl lifecycle)
    {
        var states = new JsonArray();
        foreach (var s in lifecycle.States)
        {
            states.Add(JsonValue.Create(s));
        }
        return new JsonObject
        {
            ["type"] = "string",
            ["enum"] = states,
        };
    }
}
