using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.Sample.Outline.Render;

/// <summary>
/// Markdown renderer for one <see cref="ValueTypeDecl"/>. Emits H1 + a single
/// fields table. Intentionally minimal — value types are not the focus of the
/// sample (FR-221 only mandates entities); this completes the round-out.
/// </summary>
internal static class ValueTypeOutlineRenderer
{
    private const string Lf = "\n";

    /// <summary>Render <paramref name="vt"/> at <paramref name="version"/> as a Markdown document.</summary>
    public static string Render(ValueTypeDecl vt, int version)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(vt.Name).Append('@')
          .Append(version.ToString(CultureInfo.InvariantCulture)).Append(Lf);
        sb.Append(Lf);
        sb.Append("## Fields").Append(Lf).Append(Lf);
        if (vt.Fields.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return sb.ToString();
        }
        sb.Append("| name | type |").Append(Lf);
        sb.Append("| --- | --- |").Append(Lf);
        foreach (var f in vt.Fields)
        {
            sb.Append("| ").Append(f.Name)
              .Append(" | ").Append(TypeRenderer.Render(f.Type))
              .Append(" |").Append(Lf);
        }
        sb.Append(Lf);
        return sb.ToString();
    }
}
