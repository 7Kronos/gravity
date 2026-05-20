using System.Collections.Generic;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// FR-424 — renders the per-entity lifecycle-state enum type
/// (<c>&lt;entity_name&gt;[_v&lt;N&gt;]_state</c>) wrapped in the standard
/// idempotency envelope. Emitted inline above the entity's
/// <c>CREATE TABLE</c>.
/// </summary>
internal static class LifecycleStateRenderer
{
    /// <summary>Compute the PG type name used for an entity's state enum (single- or multi-version).</summary>
    public static string TypeName(string snakeTable) => snakeTable + "_state";

    /// <summary>
    /// Render the idempotent <c>CREATE TYPE … AS ENUM (…)</c> block for
    /// <paramref name="entity"/>'s lifecycle states. Variants appear in DSL
    /// declaration order (FR-422 / FR-454). Returns an empty string when the
    /// entity declares no lifecycle states (the grammar guarantees at least
    /// one in practice).
    /// </summary>
    public static string Render(EntityDecl entity, PostgresDdlEmitterConfig cfg, string tableName)
    {
        string typeName = TypeName(tableName);
        var variants = new List<string>(entity.Lifecycle.States.Length);
        foreach (var s in entity.Lifecycle.States)
        {
            variants.Add(Identifier.QuotePgString(s));
        }
        var sb = new StringBuilder();
        sb.Append("CREATE TYPE ").Append(cfg.Schema).Append('.').Append(typeName).Append(" AS ENUM (").Append(SqlWriter.Lf);
        for (int i = 0; i < variants.Count; i++)
        {
            sb.Append(SqlWriter.Indent).Append(variants[i]);
            if (i < variants.Count - 1) sb.Append(',');
            sb.Append(SqlWriter.Lf);
        }
        sb.Append(");");
        return SqlWriter.WrapIdempotent(sb.ToString());
    }
}
