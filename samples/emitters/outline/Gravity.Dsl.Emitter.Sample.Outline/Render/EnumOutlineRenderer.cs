using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.Sample.Outline.Render;

/// <summary>
/// Markdown renderer for one <see cref="EnumDecl"/>. Emits H1 + a bullet list
/// of variants in declaration order. Intentionally minimal (FR-221).
/// </summary>
internal static class EnumOutlineRenderer
{
    private const string Lf = "\n";

    /// <summary>Render <paramref name="en"/> at <paramref name="version"/> as a Markdown document.</summary>
    public static string Render(EnumDecl en, int version)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(en.Name).Append('@')
          .Append(version.ToString(CultureInfo.InvariantCulture)).Append(Lf);
        sb.Append(Lf);
        sb.Append("## Variants").Append(Lf).Append(Lf);
        if (en.Variants.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return sb.ToString();
        }
        foreach (var v in en.Variants)
        {
            sb.Append("- ").Append(v).Append(Lf);
        }
        sb.Append(Lf);
        return sb.ToString();
    }
}
