using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.Sample.Outline.Render;

/// <summary>
/// Renders a <see cref="TypeRef"/> as a short source-form string for Markdown
/// tables (<c>String?</c>, <c>UUID</c>, <c>Money@2</c>, <c>Address[]?</c>). The
/// shape mirrors the Phase 8 <c>SourceWriter.WriteTypeRef</c> convention but is
/// kept local to the sample so a copy-paste author does not have to depend on
/// the compiler's parsing namespace.
/// </summary>
internal static class TypeRenderer
{
    /// <summary>Render <paramref name="typeRef"/> as a short source-form string.</summary>
    public static string Render(TypeRef typeRef)
    {
        var sb = new StringBuilder();
        switch (typeRef)
        {
            case PrimitiveTypeRef p:
                sb.Append(PrimitiveName(p.Kind));
                if (p.IsOptional) sb.Append('?');
                if (p.IsArray) sb.Append("[]");
                break;
            case NamedTypeRef n:
                sb.Append(n.Name);
                if (n.Version is { } v)
                {
                    sb.Append('@').Append(v.ToString(CultureInfo.InvariantCulture));
                }
                if (n.IsOptional) sb.Append('?');
                if (n.IsArray) sb.Append("[]");
                break;
            default:
                sb.Append('?');
                break;
        }
        return sb.ToString();
    }

    private static string PrimitiveName(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.String => "String",
        PrimitiveKind.Int => "Int",
        PrimitiveKind.Long => "Long",
        PrimitiveKind.Decimal => "Decimal",
        PrimitiveKind.Boolean => "Boolean",
        PrimitiveKind.Date => "Date",
        PrimitiveKind.DateTime => "DateTime",
        PrimitiveKind.Uuid => "UUID",
        _ => "?"
    };
}
