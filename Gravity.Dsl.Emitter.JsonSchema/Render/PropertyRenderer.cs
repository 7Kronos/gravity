using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter.JsonSchema.Render;

/// <summary>
/// Composes <see cref="TypeMapper"/> and <see cref="AnnotationFolder"/> into a
/// single per-property fragment. Used by every renderer that emits a
/// <c>properties</c> map.
/// </summary>
internal static class PropertyRenderer
{
    /// <summary>
    /// Render a property/field/argument's schema fragment. Folds
    /// <c>@json_schema</c> annotations (if any). Returns the property's value
    /// schema; the property name is the dictionary key in the enclosing
    /// <c>properties</c> map.
    /// </summary>
    public static JsonNode Render(
        string propertyName,
        TypeRef typeRef,
        ImmutableArray<AnnotationDecl> annotations,
        string? referrerNamespace,
        ResolvedModel model,
        IReadOnlySet<string> multiVersionFqns,
        string ownerFqn,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        JsonNode fragment;
        if (typeRef is PrimitiveTypeRef p)
        {
            var inner = TypeMapper.MapPrimitive(p.Kind);
            fragment = TypeMapper.WrapTypeRef(inner, p);
        }
        else if (typeRef is NamedTypeRef n)
        {
            var inner = TypeMapper.MapNamedType(n, referrerNamespace, model, multiVersionFqns);
            fragment = TypeMapper.WrapTypeRef(inner, n);
        }
        else
        {
            // Defensive: AST should only ever produce these two TypeRef shapes.
            fragment = new JsonObject { ["type"] = "string" };
        }

        // Annotation folding applies to JsonObject fragments. $ref short-circuits
        // in the writer. When the fragment is an array wrapper, AnnotationFolder
        // routes item-level constraint keys (pattern, minLength, etc.) onto the
        // inner items schema; array-level keys (description, examples) stay on
        // the wrapper.
        if (fragment is JsonObject obj)
        {
            AnnotationFolder.FoldOntoProperty(obj, annotations, propertyName, ownerFqn, diags);
        }
        return fragment;
    }

    /// <summary>
    /// Render a field with no annotations (value-type fields, event payload
    /// fields, command argument fields). Convenience overload — the AST's
    /// <see cref="FieldDecl"/> has no <c>Annotations</c> property in Phase 0–3
    /// per FR-053.
    /// </summary>
    public static JsonNode RenderField(
        string fieldName,
        TypeRef typeRef,
        string? referrerNamespace,
        ResolvedModel model,
        IReadOnlySet<string> multiVersionFqns,
        string ownerFqn,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        return Render(
            fieldName, typeRef,
            ImmutableArray<AnnotationDecl>.Empty,
            referrerNamespace, model, multiVersionFqns, ownerFqn, diags);
    }
}
