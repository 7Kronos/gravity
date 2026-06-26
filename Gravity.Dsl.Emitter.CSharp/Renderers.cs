using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.CSharp;

/// <summary>
/// Builds the pre-format C# source for each Gravity declaration kind. The output
/// is fed through <see cref="CSharpFileFormatter"/> so indentation, line breaks,
/// and using ordering are normalised by Roslyn before commit.
/// </summary>
internal static class Renderers
{
    /// <summary>
    /// Render a value-type declaration as a positional <c>public sealed record</c>.
    /// Each field becomes a positional parameter using its C# type from <see cref="TypeMapper"/>.
    /// </summary>
    public static string RenderValueType(
        ValueTypeDecl vt,
        string csharpNamespace,
        bool fileScopedNamespaces)
    {
        var usings = CollectUsings(vt.Fields.Select(f => f.Type));
        var sb = new StringBuilder();
        AppendUsings(sb, usings);
        AppendNamespaceOpen(sb, csharpNamespace, fileScopedNamespaces);
        AppendXmlDocFromAnnotations(sb, vt.Annotations);
        sb.Append("public sealed record ").Append(vt.Name).Append('(');
        for (int i = 0; i < vt.Fields.Length; i++)
        {
            var f = vt.Fields[i];
            if (i > 0) sb.Append(", ");
            sb.Append(TypeMapper.Render(f.Type)).Append(' ').Append(Pascal(f.Name));
        }
        sb.Append(");\n");
        AppendNamespaceClose(sb, fileScopedNamespaces);
        return sb.ToString();
    }

    /// <summary>Render an enum declaration as a plain <c>public enum</c>.</summary>
    public static string RenderEnum(
        EnumDecl en,
        string csharpNamespace,
        bool fileScopedNamespaces)
    {
        var sb = new StringBuilder();
        AppendNamespaceOpen(sb, csharpNamespace, fileScopedNamespaces);
        AppendXmlDocFromAnnotations(sb, en.Annotations);
        sb.Append("public enum ").Append(en.Name).Append("\n{\n");
        for (int i = 0; i < en.Variants.Length; i++)
        {
            sb.Append("    ").Append(en.Variants[i]);
            if (i < en.Variants.Length - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("}\n");
        AppendNamespaceClose(sb, fileScopedNamespaces);
        return sb.ToString();
    }

    /// <summary>Render the per-entity state enum (<c>&lt;EntityName&gt;State</c>).</summary>
    public static string RenderStateEnum(
        EntityDecl entity,
        string csharpNamespace,
        bool fileScopedNamespaces)
    {
        var sb = new StringBuilder();
        AppendNamespaceOpen(sb, csharpNamespace, fileScopedNamespaces);
        sb.Append("/// <summary>Lifecycle states for <see cref=\"").Append(entity.Name).Append("\"/>.</summary>\n");
        sb.Append("public enum ").Append(entity.Name).Append("State\n{\n");
        for (int i = 0; i < entity.Lifecycle.States.Length; i++)
        {
            sb.Append("    ").Append(entity.Lifecycle.States[i]);
            if (i < entity.Lifecycle.States.Length - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("}\n");
        AppendNamespaceClose(sb, fileScopedNamespaces);
        return sb.ToString();
    }

    /// <summary>
    /// Render the entity record (identity, properties, relations). The state enum
    /// lives in a separate file; this record references it via a <c>State</c> property.
    /// </summary>
    public static string RenderEntityRecord(
        EntityDecl entity,
        string csharpNamespace,
        bool fileScopedNamespaces,
        string? dslNamespace = null,
        IReadOnlyDictionary<string, string>? identityTypeByFqn = null)
    {
        var typesUsed = new List<TypeRef> { entity.Identity.Type };
        foreach (var p in entity.Properties) typesUsed.Add(p.Type);
        // Relations contribute ImmutableArray<T> for many cardinality.
        var hasManyRelation = entity.Relations.Any(r => r.Cardinality == Cardinality.Many);
        var usings = CollectUsings(typesUsed);
        if (hasManyRelation)
        {
            usings.Add("System.Collections.Immutable");
        }
        // A relation FK is typed by the TARGET entity's identity, so a Guid/DateTime/
        // DateOnly element type must pull in `using System;` even when no identity or
        // property contributes it (e.g. a String-identity entity referencing a
        // UUID-identity target). Collect from the resolved element types.
        foreach (var r in entity.Relations)
        {
            AddUsingForCSharpElement(
                RelationIdentityType(r, dslNamespace, identityTypeByFqn), usings);
        }

        var sb = new StringBuilder();
        AppendUsings(sb, usings);
        AppendNamespaceOpen(sb, csharpNamespace, fileScopedNamespaces);
        AppendXmlDocFromAnnotations(sb, entity.Annotations);
        sb.Append("public sealed record ").Append(entity.Name).Append("(\n");

        var parts = new List<string>();
        parts.Add(IndentLine(TypeMapper.Render(entity.Identity.Type) + " " + Pascal(entity.Identity.FieldName)));
        foreach (var p in entity.Properties)
        {
            parts.Add(IndentLine(TypeMapper.Render(p.Type) + " " + Pascal(p.Name)));
        }
        foreach (var r in entity.Relations)
        {
            parts.Add(IndentLine(RelationCSharpType(r, dslNamespace, identityTypeByFqn) + " " + Pascal(r.Name)));
        }
        // The current lifecycle state is part of the entity's surface.
        parts.Add(IndentLine(entity.Name + "State State"));

        for (int i = 0; i < parts.Count; i++)
        {
            sb.Append(parts[i]);
            if (i < parts.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(");\n");
        AppendNamespaceClose(sb, fileScopedNamespaces);
        return sb.ToString();
    }

    /// <summary>Render one file containing every event record for the entity.</summary>
    public static string RenderEvents(
        EntityDecl entity,
        string csharpNamespace,
        bool fileScopedNamespaces)
    {
        var allTypes = entity.Events.SelectMany(e => e.Payload.Select(p => p.Type));
        var usings = CollectUsings(allTypes);

        var sb = new StringBuilder();
        AppendUsings(sb, usings);
        AppendNamespaceOpen(sb, csharpNamespace, fileScopedNamespaces);
        for (int i = 0; i < entity.Events.Length; i++)
        {
            var evt = entity.Events[i];
            if (i > 0) sb.Append('\n');
            sb.Append("/// <summary>Domain event emitted by <see cref=\"").Append(entity.Name).Append("\"/>.</summary>\n");
            sb.Append("public sealed record ").Append(evt.Name).Append('(');
            for (int j = 0; j < evt.Payload.Length; j++)
            {
                var f = evt.Payload[j];
                if (j > 0) sb.Append(", ");
                sb.Append(TypeMapper.Render(f.Type)).Append(' ').Append(Pascal(f.Name));
            }
            sb.Append(");\n");
        }
        AppendNamespaceClose(sb, fileScopedNamespaces);
        return sb.ToString();
    }

    /// <summary>Render one file containing every command record for the entity.</summary>
    public static string RenderCommands(
        EntityDecl entity,
        string csharpNamespace,
        bool fileScopedNamespaces)
    {
        var allTypes = entity.Commands.SelectMany(c => c.Arguments.Select(a => a.Type));
        var usings = CollectUsings(allTypes);

        var sb = new StringBuilder();
        AppendUsings(sb, usings);
        AppendNamespaceOpen(sb, csharpNamespace, fileScopedNamespaces);
        for (int i = 0; i < entity.Commands.Length; i++)
        {
            var cmd = entity.Commands[i];
            if (i > 0) sb.Append('\n');
            sb.Append("/// <summary>\n");
            sb.Append("/// Command on <see cref=\"").Append(entity.Name).Append("\"/>.\n");
            sb.Append("/// Returns: ").Append(cmd.ReturnsType).Append('\n');
            sb.Append("/// Side effect: ").Append(cmd.SideEffectEvent).Append('\n');
            sb.Append("/// </summary>\n");
            sb.Append("public sealed record ").Append(cmd.Name).Append('(');
            for (int j = 0; j < cmd.Arguments.Length; j++)
            {
                var f = cmd.Arguments[j];
                if (j > 0) sb.Append(", ");
                sb.Append(TypeMapper.Render(f.Type)).Append(' ').Append(Pascal(f.Name));
            }
            sb.Append(");\n");
        }
        AppendNamespaceClose(sb, fileScopedNamespaces);
        return sb.ToString();
    }

    /// <summary>
    /// C# type for a relation foreign key. A relation is an FK to the TARGET
    /// entity's identity, so the element type is the target identity's C# type
    /// (<c>Guid</c> for UUID identities, <c>string</c> for String identities, …).
    /// Cardinality-many surfaces <c>ImmutableArray&lt;T&gt;</c>; cardinality-one is
    /// nullable when optional.
    /// </summary>
    private static string RelationCSharpType(
        RelationDecl r,
        string? referrerNamespace,
        IReadOnlyDictionary<string, string>? identityTypeByFqn)
    {
        var idType = RelationIdentityType(r, referrerNamespace, identityTypeByFqn);
        if (r.Cardinality == Cardinality.Many)
        {
            return "ImmutableArray<" + idType + ">";
        }
        // Cardinality one. Optional → nullable.
        return r.IsOptional ? idType + "?" : idType;
    }

    /// <summary>
    /// Resolve the element C# type for a relation FK: the TARGET entity's identity
    /// C# type. Falls back to <c>Guid</c> when the target cannot be resolved (which
    /// should not happen for a model that passed resolution) so emission never crashes.
    /// </summary>
    private static string RelationIdentityType(
        RelationDecl r,
        string? referrerNamespace,
        IReadOnlyDictionary<string, string>? identityTypeByFqn)
    {
        if (identityTypeByFqn is not null)
        {
            var fqn = ResolveTargetFqn(r.TargetEntity, referrerNamespace, identityTypeByFqn.Keys);
            if (fqn is not null && identityTypeByFqn.TryGetValue(fqn, out var idType))
            {
                return idType;
            }
        }
        return "Guid";
    }

    /// <summary>
    /// Best-effort simple-name → FQN resolution against the known entity FQNs.
    /// Prefers a target in the referrer's own namespace; otherwise returns the
    /// lexicographically-first FQN with the matching simple name. The resolver
    /// upstream already gates references — this only re-derives the FQN the
    /// emitter needs to look up the target identity type.
    /// </summary>
    private static string? ResolveTargetFqn(
        string simpleName,
        string? referrerNamespace,
        IEnumerable<string> candidateFqns)
    {
        string? sameNsMatch = null;
        string? anyMatch = null;
        foreach (var key in candidateFqns)
        {
            int lastDot = key.LastIndexOf('.');
            string simple = lastDot < 0 ? key : key.Substring(lastDot + 1);
            if (!string.Equals(simple, simpleName, StringComparison.Ordinal)) continue;
            string ns = lastDot < 0 ? string.Empty : key.Substring(0, lastDot);
            if (referrerNamespace != null && string.Equals(ns, referrerNamespace, StringComparison.Ordinal))
            {
                sameNsMatch = key;
                break;
            }
            if (anyMatch == null || string.CompareOrdinal(key, anyMatch) < 0)
            {
                anyMatch = key;
            }
        }
        return sameNsMatch ?? anyMatch;
    }

    /// <summary>
    /// Add the namespace a rendered C# element type needs. Only the BCL value types
    /// emitted by <see cref="TypeMapper"/> (<c>Guid</c>, <c>DateTime</c>,
    /// <c>DateOnly</c>) require <c>using System;</c>; primitives like <c>string</c>
    /// and <c>int</c> need none.
    /// </summary>
    private static void AddUsingForCSharpElement(string csharpType, SortedSet<string> set)
    {
        switch (csharpType)
        {
            case "Guid":
            case "DateTime":
            case "DateOnly":
                set.Add("System");
                break;
        }
    }

    private static SortedSet<string> CollectUsings(IEnumerable<TypeRef> types)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in types)
        {
            AddUsingsFor(t, set);
        }
        return set;
    }

    private static void AddUsingsFor(TypeRef t, SortedSet<string> set)
    {
        if (t is PrimitiveTypeRef p)
        {
            switch (p.Kind)
            {
                case PrimitiveKind.DateTime:
                case PrimitiveKind.Date:
                case PrimitiveKind.Uuid:
                    set.Add("System");
                    break;
            }
            if (p.IsArray) set.Add("System.Collections.Immutable");
        }
        else if (t is NamedTypeRef n)
        {
            if (n.IsArray) set.Add("System.Collections.Immutable");
        }
        else if (t is MapTypeRef m)
        {
            // Maps render as ImmutableDictionary<,>; recurse so the key/value
            // types pull in their own usings (e.g. Guid -> System).
            set.Add("System.Collections.Immutable");
            AddUsingsFor(m.Key, set);
            AddUsingsFor(m.Value, set);
        }
    }

    private static void AppendUsings(StringBuilder sb, SortedSet<string> usings)
    {
        if (usings.Count == 0) return;
        foreach (var u in usings)
        {
            sb.Append("using ").Append(u).Append(";\n");
        }
        sb.Append('\n');
    }

    private static void AppendNamespaceOpen(StringBuilder sb, string ns, bool fileScoped)
    {
        if (fileScoped)
        {
            sb.Append("namespace ").Append(ns).Append(";\n\n");
        }
        else
        {
            sb.Append("namespace ").Append(ns).Append("\n{\n");
        }
    }

    private static void AppendNamespaceClose(StringBuilder sb, bool fileScoped)
    {
        if (!fileScoped)
        {
            sb.Append("}\n");
        }
    }

    private static void AppendXmlDocFromAnnotations(
        StringBuilder sb,
        ImmutableArray<AnnotationDecl> annotations)
    {
        // Only annotations in the `csharp` namespace are surfaced as XML doc;
        // unknown namespaces are silently ignored per FR-053 (Phase 0-3 reserves).
        var csharp = annotations.Where(a => string.Equals(a.Namespace, "csharp", StringComparison.Ordinal)).ToList();
        if (csharp.Count == 0) return;
        foreach (var a in csharp)
        {
            sb.Append("/// <remarks>@csharp");
            if (!string.IsNullOrEmpty(a.Name)) sb.Append('.').Append(a.Name);
            if (a.Arguments.Count > 0)
            {
                sb.Append('(');
                bool first = true;
                foreach (var kv in a.Arguments)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(kv.Key).Append("=").Append(FormatAnnotationValue(kv.Value));
                    first = false;
                }
                sb.Append(')');
            }
            sb.Append("</remarks>\n");
        }
    }

    private static string FormatAnnotationValue(AnnotationValue v) => v switch
    {
        AnnotationStringValue s => "\"" + s.Value.Replace("\"", "\\\"") + "\"",
        AnnotationIntValue i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AnnotationDecimalValue d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AnnotationBoolValue b => b.Value ? "true" : "false",
        AnnotationIdentValue id => id.Value,
        _ => "?"
    };

    private static string IndentLine(string text) => "    " + text;

    /// <summary>
    /// Convert a Gravity field name (typically snake_case) into PascalCase for use
    /// as a C# property/parameter identifier. Single-segment names are capitalised;
    /// multi-segment names join their capitalised parts.
    /// </summary>
    public static string Pascal(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split('_');
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part.Substring(1));
        }
        return sb.ToString();
    }
}
