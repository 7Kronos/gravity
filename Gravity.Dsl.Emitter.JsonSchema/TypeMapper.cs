using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.JsonSchema;

/// <summary>
/// DSL primitive / named-type / array / optional → JSON Schema fragment. A
/// closed-form pure-function module — no I/O, no diagnostics surfacing, no
/// mutable state.
/// </summary>
internal static class TypeMapper
{
    /// <summary>
    /// FR-330 primitive table. Pure function: input determines output byte-for-byte.
    /// Modifiers (FR-331 / FR-332) are applied by <see cref="WrapTypeRef"/>; this
    /// method emits the raw type fragment only.
    /// </summary>
    public static JsonObject MapPrimitive(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.String => new JsonObject
        {
            ["type"] = "string",
        },
        PrimitiveKind.Int => new JsonObject
        {
            ["type"] = "integer",
            ["minimum"] = int.MinValue,
            ["maximum"] = int.MaxValue,
        },
        PrimitiveKind.Long => new JsonObject
        {
            ["type"] = "integer",
            ["minimum"] = long.MinValue,
            ["maximum"] = long.MaxValue,
        },
        PrimitiveKind.Decimal => new JsonObject
        {
            ["type"] = "string",
            ["format"] = "decimal",
        },
        PrimitiveKind.Boolean => new JsonObject
        {
            ["type"] = "boolean",
        },
        PrimitiveKind.Date => new JsonObject
        {
            ["type"] = "string",
            ["format"] = "date",
        },
        PrimitiveKind.DateTime => new JsonObject
        {
            ["type"] = "string",
            ["format"] = "date-time",
        },
        PrimitiveKind.Uuid => new JsonObject
        {
            ["type"] = "string",
            ["format"] = "uuid",
        },
        _ => throw new InvalidOperationException("unknown PrimitiveKind '" + kind + "'"),
    };

    /// <summary>
    /// FR-331 / FR-332 modifier composition. Optional (<c>T?</c>) is NOT
    /// encoded in the fragment — it controls the enclosing object's
    /// <c>required</c> array (FR-331). Array (<c>T[]</c>) wraps the inner
    /// fragment in <c>{ "type": "array", "items": &lt;inner&gt; }</c>;
    /// <c>T?[]</c> and <c>T[]</c> produce identical fragments (FR-332's
    /// documented asymmetry — JSON arrays have no absent slots).
    /// </summary>
    public static JsonNode WrapTypeRef(JsonObject inner, TypeRef typeRef)
    {
        bool isArray = typeRef switch
        {
            PrimitiveTypeRef p => p.IsArray,
            NamedTypeRef n => n.IsArray,
            _ => false,
        };
        if (isArray)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = inner,
            };
        }
        return inner;
    }

    /// <summary>
    /// FR-314 step 3 relation encoding. Cardinality-one → <c>"&lt;name&gt;_id"</c>
    /// UUID property; cardinality-many → <c>"&lt;name&gt;_ids"</c> UUID array
    /// with <c>uniqueItems: true</c>. The relation's <c>Semantic</c> clause
    /// folds into the property entry's <c>description</c> field.
    /// </summary>
    public static (string PropertyName, JsonObject Fragment) MapEntityRelation(RelationDecl rel)
    {
        bool many = rel.Cardinality == Cardinality.Many;
        string propName = rel.Name + (many ? "_ids" : "_id");
        JsonObject fragment = many
            ? new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uuid",
                },
                ["uniqueItems"] = true,
            }
            : new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
            };
        string description = rel.Semantic is string s && !string.IsNullOrEmpty(s)
            ? "references " + rel.TargetEntity + " by id (" + s + ")"
            : "references " + rel.TargetEntity + " by id";
        fragment["description"] = description;
        return (propName, fragment);
    }

    /// <summary>
    /// FR-318 two-tier $ref. Cross-file refs use <c>"&lt;Name&gt;.json"</c>
    /// (same-namespace) or <c>"../&lt;other-ns-path&gt;/&lt;Name&gt;.json"</c>
    /// (cross-namespace). Within-bundle refs would use
    /// <c>"#/definitions/&lt;Name&gt;"</c>; no v1 grammar produces them so the
    /// helper always emits cross-file refs (per FR-333: NamedTypeRef points to
    /// ValueTypeDecl or EnumDecl, both namespace-scope).
    /// </summary>
    public static JsonObject MapNamedType(
        NamedTypeRef named,
        string? referrerNamespace,
        ResolvedModel model,
        IReadOnlySet<string> multiVersionFqns)
    {
        var (targetNamespace, targetName) = ResolveTarget(named, referrerNamespace, model);
        string fqn = string.IsNullOrEmpty(targetNamespace) ? targetName : targetNamespace + "." + targetName;
        string fileName = multiVersionFqns.Contains(fqn) && named.Version is int v
            ? targetName + ".v" + v.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json"
            : targetName + ".json";
        string refPath = string.Equals(targetNamespace ?? string.Empty, referrerNamespace ?? string.Empty, StringComparison.Ordinal)
            ? fileName
            : ComposeRelativeRefPath(referrerNamespace, targetNamespace, fileName);
        return new JsonObject
        {
            ["$ref"] = refPath,
        };
    }

    /// <summary>
    /// Resolve a <see cref="NamedTypeRef"/> to its (namespace, simpleName)
    /// pair. The Phase 0–3 resolver guarantees a single match for legal
    /// source; we walk <see cref="ResolvedModel.Declarations"/> to find an
    /// FQN whose simple-name matches. Preferred match is in the referrer's
    /// own namespace; failing that, the first ordinal match wins.
    /// </summary>
    public static (string? Namespace, string Name) ResolveTarget(
        NamedTypeRef named,
        string? referrerNamespace,
        ResolvedModel model)
    {
        // First-pass: same-namespace match preferred (Phase 0–3 resolver shape).
        string? sameNsMatchNs = null;
        string? sameNsMatchName = null;
        string? fallbackNs = null;
        string? fallbackName = null;
        foreach (var kv in model.Declarations)
        {
            string fqn = kv.Key.Fqn;
            int dot = fqn.LastIndexOf('.');
            string ns = dot >= 0 ? fqn.Substring(0, dot) : string.Empty;
            string simple = dot >= 0 ? fqn.Substring(dot + 1) : fqn;
            if (!string.Equals(simple, named.Name, StringComparison.Ordinal)) continue;
            if (string.Equals(ns, referrerNamespace ?? string.Empty, StringComparison.Ordinal))
            {
                sameNsMatchNs = ns;
                sameNsMatchName = simple;
                break;
            }
            if (fallbackName is null)
            {
                fallbackNs = ns;
                fallbackName = simple;
            }
        }
        if (sameNsMatchName is not null) return (sameNsMatchNs, sameNsMatchName);
        if (fallbackName is not null) return (fallbackNs, fallbackName);
        // Unresolved at the emitter layer — the resolver/validator should have
        // caught this. Return the as-written name verbatim so the cross-file
        // $ref still shapes (consumer validation will then fail loudly on a
        // missing target file).
        return (referrerNamespace, named.Name);
    }

    /// <summary>
    /// Compose a relative cross-namespace ref path. Both namespaces map to
    /// <c>dotted.segments → segments/path</c> directories; the result walks
    /// up from the referrer to the common ancestor, then down to the target.
    /// Each dotted segment is defense-in-depth checked against
    /// path-traversal sequences (the grammar already forbids them at parse
    /// time).
    /// </summary>
    private static string ComposeRelativeRefPath(string? referrerNamespace, string? targetNamespace, string fileName)
    {
        if (!string.IsNullOrEmpty(referrerNamespace))
        {
            foreach (var seg in referrerNamespace.Split('.')) JsonSchemaEmitter.ValidateNamespaceSegment(seg);
        }
        if (!string.IsNullOrEmpty(targetNamespace))
        {
            foreach (var seg in targetNamespace.Split('.')) JsonSchemaEmitter.ValidateNamespaceSegment(seg);
        }
        string fromPath = (referrerNamespace ?? string.Empty).Replace('.', '/');
        string toPath = (targetNamespace ?? string.Empty).Replace('.', '/');
        var fromParts = string.IsNullOrEmpty(fromPath) ? Array.Empty<string>() : fromPath.Split('/');
        var toParts = string.IsNullOrEmpty(toPath) ? Array.Empty<string>() : toPath.Split('/');
        int common = 0;
        while (common < fromParts.Length && common < toParts.Length
               && string.Equals(fromParts[common], toParts[common], StringComparison.Ordinal))
        {
            common++;
        }
        var sb = new System.Text.StringBuilder();
        for (int i = common; i < fromParts.Length; i++)
        {
            sb.Append("../");
        }
        for (int i = common; i < toParts.Length; i++)
        {
            sb.Append(toParts[i]).Append('/');
        }
        sb.Append(fileName);
        string result = sb.ToString();
        return result.Length == 0 ? fileName : result;
    }
}
