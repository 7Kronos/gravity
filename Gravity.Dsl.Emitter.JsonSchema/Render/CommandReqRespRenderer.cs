using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// FR-312 / FR-313 — renders the per-command request + response
/// <c>definitions</c> entries inside the entity bundle. The request schema
/// captures arguments; the response schema is always a <c>$ref</c>
/// indirection (FR-313).
/// </summary>
internal static class CommandReqRespRenderer
{
    public static JsonObject RenderRequest(
        CommandDecl cmd,
        string entityFqn,
        string? referrerNamespace,
        ResolvedModel model,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var a in cmd.Arguments)
        {
            var frag = PropertyRenderer.RenderField(
                a.Name, a.Type, referrerNamespace, model, multiVersionFqns,
                entityFqn + "." + cmd.Name + ".Request", diags);
            properties[a.Name] = frag;
            bool optional = a.Type switch
            {
                PrimitiveTypeRef p => p.IsOptional,
                NamedTypeRef n => n.IsOptional,
                _ => false,
            };
            if (!optional)
            {
                required.Add(JsonValue.Create(a.Name));
            }
        }
        return new JsonObject
        {
            ["title"] = entityFqn + "." + cmd.Name + ".Request",
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }

    public static JsonObject RenderResponse(
        CommandDecl cmd,
        string? referrerNamespace,
        ResolvedModel model,
        IReadOnlySet<string> multiVersionFqns)
    {
        // FR-313: response is always a $ref. Phase 8 forbids @N on `returns`,
        // so the type ref carries Version=null; MapNamedType resolves the
        // target's namespace + multi-version layout.
        var synthetic = new NamedTypeRef(
            Name: cmd.ReturnsType,
            IsOptional: false,
            IsArray: false,
            Span: cmd.Span,
            Version: null);
        return TypeMapper.MapNamedType(synthetic, referrerNamespace, model, multiVersionFqns);
    }
}
