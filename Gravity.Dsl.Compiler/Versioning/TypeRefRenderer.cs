using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;

namespace Gravity.Dsl.Compiler.Versioning;

/// <summary>
/// Renders a <see cref="TypeRef"/> to its canonical source-form string
/// (<c>String?[]</c>, <c>Money@2</c>, <c>Project@1?</c>, etc.) for diagnostic
/// "from"/"to" messages. Delegates to <see cref="SourceWriter.WriteTypeRef"/>
/// so the diagnostic rendering and the source-emitter rendering stay in lock-step
/// (FR-150).
/// </summary>
internal static class TypeRefRenderer
{
    /// <summary>Render a <see cref="TypeRef"/> as its canonical source-form string.</summary>
    public static string Render(TypeRef tr)
    {
        var sb = new StringBuilder();
        SourceWriter.WriteTypeRef(sb, tr);
        return sb.ToString();
    }
}
