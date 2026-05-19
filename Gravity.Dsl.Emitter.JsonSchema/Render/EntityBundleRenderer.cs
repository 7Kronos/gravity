using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// FR-310 / FR-314 / FR-315 / FR-319 — composes the entity-state root
/// schema + every <c>definitions</c> entry (events, command requests +
/// responses, lifecycle state enum) into a single byte-deterministic Draft-07
/// bundle document.
/// </summary>
internal static class EntityBundleRenderer
{
    public static string Render(
        EntityDecl entity,
        ResolvedModel model,
        DeclKey key,
        string? referrerNamespace,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        // (1) Identity field (FR-314 step 1).
        if (entity.Identity.Type is PrimitiveTypeRef identPrim)
        {
            properties[entity.Identity.FieldName] = TypeMapper.MapPrimitive(identPrim.Kind);
        }
        else if (entity.Identity.Type is NamedTypeRef identNamed)
        {
            properties[entity.Identity.FieldName] = TypeMapper.MapNamedType(
                identNamed, referrerNamespace, model, multiVersionFqns);
        }
        required.Add(JsonValue.Create(entity.Identity.FieldName));

        // (2) Each PropertyDecl (FR-314 step 2). JS003: user property collides
        // with reserved `state` slot (FR-315).
        foreach (var p in entity.Properties)
        {
            if (string.Equals(p.Name, "state", StringComparison.Ordinal))
            {
                diags.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    JsonRuleIds.Js003,
                    "entity '" + key.Fqn
                        + "' declares a property named 'state' that collides with the reserved entity-state property",
                    p.Span));
                continue;
            }
            var frag = PropertyRenderer.Render(
                p.Name, p.Type, p.Annotations,
                referrerNamespace, model, multiVersionFqns, key.Fqn, diags);
            properties[p.Name] = frag;
            bool optional = p.Type switch
            {
                PrimitiveTypeRef pp => pp.IsOptional,
                NamedTypeRef pn => pn.IsOptional,
                _ => false,
            };
            if (!optional)
            {
                required.Add(JsonValue.Create(p.Name));
            }
        }

        // (3) Each RelationDecl (FR-314 step 3): cardinality-one → _id;
        //     cardinality-many → _ids; optional → omitted from `required`.
        foreach (var r in entity.Relations)
        {
            var (relName, relFragment) = TypeMapper.MapEntityRelation(r);
            properties[relName] = relFragment;
            if (!r.IsOptional)
            {
                required.Add(JsonValue.Create(relName));
            }
        }

        // (4) Reserved `state` slot (FR-315) — $ref to the local State enum
        // definition. Always required.
        properties["state"] = new JsonObject
        {
            ["$ref"] = "#/definitions/" + entity.Name + "State",
        };
        required.Add(JsonValue.Create("state"));

        // (5) Build the definitions map.
        var defs = new JsonObject();
        foreach (var ev in entity.Events)
        {
            defs[ev.Name] = EventPayloadRenderer.Render(
                ev, key.Fqn, referrerNamespace, model, multiVersionFqns, diags);
        }
        foreach (var c in entity.Commands)
        {
            defs[c.Name + "Request"] = CommandReqRespRenderer.RenderRequest(
                c, key.Fqn, referrerNamespace, model, multiVersionFqns, diags);
            defs[c.Name + "Response"] = CommandReqRespRenderer.RenderResponse(
                c, referrerNamespace, model, multiVersionFqns);
        }
        defs[entity.Name + "State"] = LifecycleStateRenderer.Render(entity.Lifecycle);

        var doc = new JsonObject
        {
            ["$schema"] = JsonSchemaEmitter.Draft07Uri,
            ["title"] = key.Fqn,
            ["x-gravity-version"] = key.Version,
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
            ["definitions"] = defs,
        };
        return SortedKeyJsonWriter.Serialize(doc);
    }
}
