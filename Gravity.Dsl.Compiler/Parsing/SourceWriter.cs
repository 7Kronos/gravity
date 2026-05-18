using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Parsing;

/// <summary>
/// Canonical serializer (`AST -> .gravity source`). Output rules (T021):
/// (a) entity body sections in fixed order: identity, relations, properties, lifecycle, events, commands;
/// (b) four-space indent; (c) exactly one blank line between top-level decls and between sections;
/// (d) no trailing whitespace; (e) LF only; (f) trailing newline; (g) comments dropped;
/// (h) annotation arguments emitted in StringComparer.Ordinal order of key.
/// </summary>
public static class SourceWriter
{
    private const string Indent = "    ";
    private const char LF = '\n';

    public static string Write(SourceFile file)
    {
        var sb = new StringBuilder();
        bool needsBlank = false;

        if (file.Namespace is { } ns)
        {
            sb.Append("namespace ").Append(ns.Name).Append(';').Append(LF);
            needsBlank = true;
        }

        if (file.Imports.Length > 0)
        {
            if (needsBlank) sb.Append(LF);
            for (int i = 0; i < file.Imports.Length; i++)
            {
                sb.Append("import \"").Append(file.Imports[i].RelativePath).Append("\";").Append(LF);
            }
            needsBlank = true;
        }

        for (int i = 0; i < file.Declarations.Length; i++)
        {
            if (needsBlank) sb.Append(LF);
            WriteTopLevel(sb, file.Declarations[i]);
            needsBlank = true;
        }

        EnsureSingleTrailingNewline(sb);
        return sb.ToString();
    }

    private static void WriteTopLevel(StringBuilder sb, TopLevelDecl decl)
    {
        switch (decl)
        {
            case EntityDecl entity:
                WriteEntity(sb, entity);
                break;
            case ValueTypeDecl vt:
                WriteValueType(sb, vt);
                break;
            case EnumDecl en:
                WriteEnum(sb, en);
                break;
        }
    }

    private static void WriteValueType(StringBuilder sb, ValueTypeDecl vt)
    {
        WriteAnnotations(sb, vt.Annotations, indent: "");
        sb.Append("type ").Append(vt.Name);
        if (vt.Version != 1)
        {
            sb.Append(" version ").Append(vt.Version.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(" {").Append(LF);
        foreach (var f in vt.Fields)
        {
            sb.Append(Indent);
            WriteField(sb, f);
            sb.Append(LF);
        }
        sb.Append('}').Append(LF);
    }

    private static void WriteEnum(StringBuilder sb, EnumDecl en)
    {
        WriteAnnotations(sb, en.Annotations, indent: "");
        sb.Append("enum ").Append(en.Name);
        if (en.Version != 1)
        {
            sb.Append(" version ").Append(en.Version.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(" {").Append(LF);
        for (int i = 0; i < en.Variants.Length; i++)
        {
            sb.Append(Indent).Append(en.Variants[i]);
            if (i < en.Variants.Length - 1) sb.Append(',');
            sb.Append(LF);
        }
        sb.Append('}').Append(LF);
    }

    private static void WriteEntity(StringBuilder sb, EntityDecl entity)
    {
        WriteAnnotations(sb, entity.Annotations, indent: "");
        sb.Append("entity ").Append(entity.Name)
          .Append(" version ").Append(entity.Version.ToString(CultureInfo.InvariantCulture));
        if (entity.Deprecates is { } dep)
        {
            sb.Append(" deprecates version ")
              .Append(dep.Version.ToString(CultureInfo.InvariantCulture))
              .Append(" until \"").Append(dep.UntilIso8601).Append('"');
        }
        sb.Append(" {").Append(LF);

        bool needsBlank = false;

        // identity (always present)
        if (needsBlank) sb.Append(LF);
        WriteIdentity(sb, entity.Identity);
        needsBlank = true;

        if (entity.Relations.Length > 0)
        {
            if (needsBlank) sb.Append(LF);
            WriteRelations(sb, entity.Relations);
            needsBlank = true;
        }
        if (entity.Properties.Length > 0)
        {
            if (needsBlank) sb.Append(LF);
            WriteProperties(sb, entity.Properties);
            needsBlank = true;
        }
        if (entity.Lifecycle.States.Length > 0 || entity.Lifecycle.Transitions.Length > 0)
        {
            if (needsBlank) sb.Append(LF);
            WriteLifecycle(sb, entity.Lifecycle);
            needsBlank = true;
        }
        if (entity.Events.Length > 0)
        {
            if (needsBlank) sb.Append(LF);
            WriteEvents(sb, entity.Events);
            needsBlank = true;
        }
        if (entity.Commands.Length > 0)
        {
            if (needsBlank) sb.Append(LF);
            WriteCommands(sb, entity.Commands);
            needsBlank = true;
        }

        sb.Append('}').Append(LF);
    }

    private static void WriteIdentity(StringBuilder sb, IdentityDecl id)
    {
        sb.Append(Indent).Append("identity ").Append(id.FieldName).Append(": ");
        WriteTypeRef(sb, id.Type);
        sb.Append(';').Append(LF);
    }

    private static void WriteRelations(StringBuilder sb, ImmutableArray<RelationDecl> relations)
    {
        sb.Append(Indent).Append("relations {").Append(LF);
        foreach (var r in relations)
        {
            WriteAnnotations(sb, r.Annotations, indent: Indent + Indent);
            sb.Append(Indent).Append(Indent).Append(r.Name).Append(": ").Append(r.TargetEntity);
            if (r.IsOptional) sb.Append('?');
            sb.Append(" cardinality ").Append(r.Cardinality == Cardinality.Many ? "many" : "one");
            if (r.Semantic is { } sem) sb.Append(" semantic ").Append(sem);
            sb.Append(';').Append(LF);
        }
        sb.Append(Indent).Append('}').Append(LF);
    }

    private static void WriteProperties(StringBuilder sb, ImmutableArray<PropertyDecl> props)
    {
        sb.Append(Indent).Append("properties {").Append(LF);
        foreach (var p in props)
        {
            sb.Append(Indent).Append(Indent).Append(p.Name).Append(": ");
            WriteTypeRef(sb, p.Type);
            foreach (var a in p.Annotations)
            {
                sb.Append(' ');
                WriteAnnotationInline(sb, a);
            }
            sb.Append(';').Append(LF);
        }
        sb.Append(Indent).Append('}').Append(LF);
    }

    private static void WriteLifecycle(StringBuilder sb, LifecycleDecl lc)
    {
        sb.Append(Indent).Append("lifecycle {").Append(LF);
        sb.Append(Indent).Append(Indent).Append("states {").Append(LF);
        if (lc.States.Length > 0)
        {
            sb.Append(Indent).Append(Indent).Append(Indent);
            for (int i = 0; i < lc.States.Length; i++)
            {
                sb.Append(lc.States[i]);
                if (i < lc.States.Length - 1) sb.Append(", ");
            }
            sb.Append(';').Append(LF);
        }
        sb.Append(Indent).Append(Indent).Append('}').Append(LF);

        sb.Append(Indent).Append(Indent).Append("transitions {").Append(LF);
        foreach (var t in lc.Transitions)
        {
            sb.Append(Indent).Append(Indent).Append(Indent)
              .Append(t.From).Append(" -> ").Append(t.To).Append(" on ").Append(t.OnEvent).Append(';').Append(LF);
        }
        sb.Append(Indent).Append(Indent).Append('}').Append(LF);
        sb.Append(Indent).Append('}').Append(LF);
    }

    private static void WriteEvents(StringBuilder sb, ImmutableArray<EventDecl> events)
    {
        sb.Append(Indent).Append("events {").Append(LF);
        foreach (var e in events)
        {
            sb.Append(Indent).Append(Indent).Append(e.Name).Append(" {");
            if (e.Payload.Length == 0)
            {
                sb.Append("};").Append(LF);
                continue;
            }
            sb.Append(LF);
            foreach (var f in e.Payload)
            {
                sb.Append(Indent).Append(Indent).Append(Indent);
                WriteField(sb, f);
                sb.Append(LF);
            }
            sb.Append(Indent).Append(Indent).Append("};").Append(LF);
        }
        sb.Append(Indent).Append('}').Append(LF);
    }

    private static void WriteCommands(StringBuilder sb, ImmutableArray<CommandDecl> commands)
    {
        sb.Append(Indent).Append("commands {").Append(LF);
        foreach (var c in commands)
        {
            sb.Append(Indent).Append(Indent).Append(c.Name).Append('(');
            for (int i = 0; i < c.Arguments.Length; i++)
            {
                var arg = c.Arguments[i];
                sb.Append(arg.Name).Append(": ");
                WriteTypeRef(sb, arg.Type);
                if (i < c.Arguments.Length - 1) sb.Append(", ");
            }
            sb.Append(')').Append(LF);
            sb.Append(Indent).Append(Indent).Append(Indent)
              .Append("returns ").Append(c.ReturnsType).Append(LF);
            sb.Append(Indent).Append(Indent).Append(Indent)
              .Append("with side_effect ").Append(c.SideEffectEvent).Append(';').Append(LF);
        }
        sb.Append(Indent).Append('}').Append(LF);
    }

    private static void WriteField(StringBuilder sb, FieldDecl f)
    {
        sb.Append(f.Name).Append(": ");
        WriteTypeRef(sb, f.Type);
        sb.Append(';');
    }

    private static void WriteTypeRef(StringBuilder sb, TypeRef tr)
    {
        switch (tr)
        {
            case PrimitiveTypeRef p:
                sb.Append(PrimitiveName(p.Kind));
                WriteTypeSuffix(sb, p.IsOptional, p.IsArray);
                break;
            case NamedTypeRef n:
                sb.Append(n.Name);
                WriteTypeSuffix(sb, n.IsOptional, n.IsArray);
                break;
        }
    }

    private static void WriteTypeSuffix(StringBuilder sb, bool isOptional, bool isArray)
    {
        // FR-011: order matters. We chose to emit '?' before '[]' to match the
        // post-parse intent for "Optional then Array". This mirrors how the AST
        // is reconstructed: an Optional+Array type is represented identically
        // regardless of source order, but we settle on '?[]' as canonical.
        // Since parser preserves the bools but not order, canonical form picks one.
        if (isOptional) sb.Append('?');
        if (isArray) sb.Append("[]");
    }

    private static string PrimitiveName(PrimitiveKind k) => k switch
    {
        PrimitiveKind.String => "String",
        PrimitiveKind.Int => "Int",
        PrimitiveKind.Long => "Long",
        PrimitiveKind.Decimal => "Decimal",
        PrimitiveKind.Boolean => "Boolean",
        PrimitiveKind.Date => "Date",
        PrimitiveKind.DateTime => "DateTime",
        PrimitiveKind.Uuid => "UUID",
        _ => k.ToString()
    };

    private static void WriteAnnotations(StringBuilder sb, ImmutableArray<AnnotationDecl> annotations, string indent)
    {
        foreach (var a in annotations)
        {
            sb.Append(indent);
            WriteAnnotationInline(sb, a);
            sb.Append(LF);
        }
    }

    private static void WriteAnnotationInline(StringBuilder sb, AnnotationDecl a)
    {
        sb.Append('@').Append(a.Namespace);
        if (!string.IsNullOrEmpty(a.Name))
        {
            sb.Append('.').Append(a.Name);
        }
        if (a.Arguments.Count > 0)
        {
            sb.Append('(');
            bool first = true;
            // ImmutableSortedDictionary iterates in key order (ordinal) — deterministic.
            foreach (var kv in a.Arguments)
            {
                if (!first) sb.Append(", ");
                sb.Append(kv.Key).Append(": ");
                WriteAnnotationValue(sb, kv.Value);
                first = false;
            }
            sb.Append(')');
        }
    }

    private static void WriteAnnotationValue(StringBuilder sb, AnnotationValue v)
    {
        switch (v)
        {
            case AnnotationStringValue sv:
                sb.Append('"');
                foreach (char ch in sv.Value)
                {
                    if (ch == '\\') sb.Append("\\\\");
                    else if (ch == '"') sb.Append("\\\"");
                    else if (ch == '\n') sb.Append("\\n");
                    else if (ch == '\r') sb.Append("\\r");
                    else if (ch == '\t') sb.Append("\\t");
                    else sb.Append(ch);
                }
                sb.Append('"');
                break;
            case AnnotationIntValue iv:
                sb.Append(iv.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case AnnotationDecimalValue dv:
                sb.Append(dv.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case AnnotationBoolValue bv:
                sb.Append(bv.Value ? "true" : "false");
                break;
            case AnnotationIdentValue idv:
                sb.Append(idv.Value);
                break;
        }
    }

    private static void EnsureSingleTrailingNewline(StringBuilder sb)
    {
        while (sb.Length > 1 && sb[sb.Length - 1] == LF && sb[sb.Length - 2] == LF)
        {
            sb.Length -= 1;
        }
        if (sb.Length == 0 || sb[sb.Length - 1] != LF)
        {
            sb.Append(LF);
        }
    }
}
