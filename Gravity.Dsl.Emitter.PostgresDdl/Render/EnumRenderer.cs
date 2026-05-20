using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// FR-422 — renders one namespace-scope enum type as a stand-alone baseline
/// DDL file. Wrapped in the standard idempotency envelope; variant order
/// preserved from the AST (semantic ordering matters).
/// </summary>
internal static class EnumRenderer
{
    public static string Render(EnumDecl en, DeclKey key, PostgresDdlEmitterConfig cfg, bool multiVersion)
    {
        string typeName = Identifier.ToSnakeCase(en.Name);
        if (multiVersion)
        {
            typeName += "_v" + key.Version.ToString(CultureInfo.InvariantCulture);
        }

        var sb = new StringBuilder();
        sb.Append("CREATE TYPE ").Append(cfg.Schema).Append('.').Append(typeName).Append(" AS ENUM (").Append(SqlWriter.Lf);
        for (int i = 0; i < en.Variants.Length; i++)
        {
            sb.Append(SqlWriter.Indent).Append(Identifier.QuotePgString(en.Variants[i]));
            if (i < en.Variants.Length - 1) sb.Append(',');
            sb.Append(SqlWriter.Lf);
        }
        sb.Append(");");

        return SqlWriter.Compose(
            SqlWriter.Header(key.Fqn, key.Version),
            SqlWriter.WrapIdempotent(sb.ToString()));
    }
}
