using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// FR-421 — renders one namespace-scope value type as a composite PG type
/// (<c>CREATE TYPE … AS (…)</c>), wrapped in the idempotency envelope.
/// Fields appear in DSL declaration order; field column names go through
/// <see cref="Identifier.ToSnakeCase"/>; PG composite-type fields do not
/// carry <c>NOT NULL</c> (composite-type field constraints are a documented
/// future enhancement — NG-8 deferrals).
/// </summary>
internal static class ValueTypeRenderer
{
    public static string Render(
        ValueTypeDecl vt,
        ResolvedModel model,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion,
        IReadOnlySet<string> multiVersionFqns)
    {
        string typeName = Identifier.ToSnakeCase(vt.Name);
        if (multiVersion)
        {
            typeName += "_v" + key.Version.ToString(CultureInfo.InvariantCulture);
        }

        // Build declToFile lazily for NamedTypeRef resolution.
        var declToFile = BuildDeclToFile(model);

        var sb = new StringBuilder();
        sb.Append("CREATE TYPE ").Append(cfg.Schema).Append('.').Append(typeName).Append(" AS (").Append(SqlWriter.Lf);
        for (int i = 0; i < vt.Fields.Length; i++)
        {
            var field = vt.Fields[i];
            string colName = Identifier.ToSnakeCase(field.Name);
            string colType = TypeMapper.MapType(field.Type, cfg, multiVersionFqns, declToFile);
            sb.Append(SqlWriter.Indent).Append(colName).Append(' ').Append(colType);
            if (i < vt.Fields.Length - 1) sb.Append(',');
            sb.Append(SqlWriter.Lf);
        }
        sb.Append(");");

        return SqlWriter.Compose(
            SqlWriter.Header(key.Fqn, key.Version),
            SqlWriter.WrapIdempotent(sb.ToString()));
    }

    private static Dictionary<string, SourceFile> BuildDeclToFile(ResolvedModel model)
    {
        var map = new Dictionary<string, SourceFile>(System.StringComparer.Ordinal);
        foreach (var file in model.Files)
        {
            string ns = file.Namespace?.Name ?? string.Empty;
            foreach (var decl in file.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                if (!map.ContainsKey(fqn)) map[fqn] = file;
            }
        }
        return map;
    }
}
