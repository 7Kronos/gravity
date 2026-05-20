using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// FR-426 / FR-427 — produces the <c>migrations/</c> tree. V1 is the full
/// baseline (identical body to the schema/ file); V&lt;N&gt; (N ≥ 2) is the
/// additive diff between vN-1 and vN. The diff is structural — additive-only
/// is guaranteed upstream by the Phase 8 breaking-change validator
/// (<c>VAL020..VAL030</c>), so the diff need never consider DROP / narrowing.
/// </summary>
internal static class MigrationRenderer
{
    public static void RenderAll(
        IReadOnlyList<(DeclKey Key, TopLevelDecl Decl)> versions,
        ResolvedModel model,
        PostgresDdlEmitterConfig cfg,
        string? dslNamespace,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns,
        IEmitterOutput sink,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        if (versions.Count == 0) return;
        bool multiVersion = versions.Count > 1;
        var firstDecl = versions[0].Decl;

        // Skip migration emission for single-version value types and enums
        // (FR-427: only emit migrations when more than one version is in scope).
        if (!multiVersion && firstDecl is not EntityDecl) return;

        for (int i = 0; i < versions.Count; i++)
        {
            var (key, decl) = versions[i];
            string? body;
            if (i == 0)
            {
                body = RenderBaseline(decl, model, key, cfg, multiVersion, declToFile, multiVersionFqns, diags);
            }
            else
            {
                body = RenderDiff(versions[i - 1].Decl, decl, key, cfg, multiVersion, declToFile, multiVersionFqns, model);
            }
            if (body is null) continue;
            sink.WriteFile(MigrationPath(cfg, dslNamespace, decl.Name, key.Version), body);
        }
    }

    private static string MigrationPath(PostgresDdlEmitterConfig cfg, string? ns, string declName, int version)
    {
        string dir = PostgresDdlEmitter.Combine(cfg.Output, PostgresDdlEmitter.Combine("migrations", PostgresDdlEmitter.ComposeDirectory(ns)));
        string file = cfg.MigrationPrefix + version.ToString(CultureInfo.InvariantCulture) + "__" + declName + ".sql";
        return PostgresDdlEmitter.Combine(dir, file);
    }

    private static string? RenderBaseline(
        TopLevelDecl decl,
        ResolvedModel model,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        return decl switch
        {
            EntityDecl e    => EntityTableRenderer.Render(e, model, key, cfg, multiVersion, declToFile, multiVersionFqns, diags),
            ValueTypeDecl v => ValueTypeRenderer.Render(v, model, key, cfg, multiVersion, multiVersionFqns),
            EnumDecl en     => EnumRenderer.Render(en, key, cfg, multiVersion),
            _ => null,
        };
    }

    private static string? RenderDiff(
        TopLevelDecl previous,
        TopLevelDecl current,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns,
        ResolvedModel model)
    {
        if (previous is EntityDecl pe && current is EntityDecl ce)
        {
            return RenderEntityDiff(pe, ce, key, cfg, multiVersion, declToFile, multiVersionFqns, model);
        }
        if (previous is ValueTypeDecl pv && current is ValueTypeDecl cv)
        {
            return RenderValueTypeDiff(pv, cv, key, cfg, multiVersion, declToFile, multiVersionFqns);
        }
        if (previous is EnumDecl pen && current is EnumDecl cen)
        {
            return RenderEnumDiff(pen, cen, key, cfg, multiVersion);
        }
        return null;
    }

    private static string RenderEntityDiff(
        EntityDecl prev,
        EntityDecl curr,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns,
        ResolvedModel model)
    {
        string tableName = EntityTableRenderer.TableName(curr, key.Version, multiVersion);
        string stateTypeName = LifecycleStateRenderer.TypeName(tableName);
        string qual = cfg.Schema + "." + tableName;

        var statements = new List<string>();

        // New properties.
        var prevPropNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var p in prev.Properties) prevPropNames.Add(p.Name);
        foreach (var p in curr.Properties)
        {
            if (prevPropNames.Contains(p.Name)) continue;
            string colName = Identifier.ToSnakeCase(p.Name);
            string colType = TypeMapper.MapType(p.Type, cfg, multiVersionFqns, declToFile);
            var sb = new StringBuilder();
            sb.Append("ALTER TABLE ").Append(qual)
              .Append(" ADD COLUMN IF NOT EXISTS ").Append(colName).Append(' ').Append(colType);
            if (!TypeMapper.IsOptional(p.Type)) sb.Append(" NOT NULL");
            sb.Append(';');
            statements.Add(sb.ToString());
        }

        // New relations.
        var prevRelNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var r in prev.Relations) prevRelNames.Add(r.Name);
        foreach (var r in curr.Relations)
        {
            if (prevRelNames.Contains(r.Name)) continue;
            var rc = TypeMapper.MapRelation(r);
            var colSb = new StringBuilder();
            colSb.Append("ALTER TABLE ").Append(qual)
                 .Append(" ADD COLUMN IF NOT EXISTS ").Append(rc.ColumnName).Append(' ').Append(rc.ColumnType);
            if (!r.IsOptional)
            {
                colSb.Append(" NOT NULL");
                if (rc.IsArrayMany) colSb.Append(" DEFAULT '{}'::").Append(rc.ColumnType);
            }
            colSb.Append(';');
            statements.Add(colSb.ToString());

            string ixName = "ix_" + tableName + "_" + rc.ColumnName;
            if (rc.IsArrayMany)
            {
                statements.Add("CREATE INDEX IF NOT EXISTS " + ixName + " ON " + qual + " USING GIN (" + rc.ColumnName + ");");
            }
            else
            {
                string targetTable = TypeMapper.ResolveFkTargetTable(r, key, declToFile, multiVersionFqns, model);
                string fkName = "fk_" + tableName + "_" + rc.ColumnName;
                string fkInner =
                    "ALTER TABLE " + qual
                    + " ADD CONSTRAINT " + fkName
                    + " FOREIGN KEY (" + rc.ColumnName + ")"
                    + " REFERENCES " + cfg.Schema + "." + targetTable + "(id);";
                statements.Add(SqlWriter.WrapIdempotent(fkInner));
                statements.Add("CREATE INDEX IF NOT EXISTS " + ixName + " ON " + qual + "(" + rc.ColumnName + ");");
            }
        }

        // New lifecycle states.
        var prevStates = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var s in prev.Lifecycle.States) prevStates.Add(s);
        foreach (var s in curr.Lifecycle.States)
        {
            if (prevStates.Contains(s)) continue;
            statements.Add("ALTER TYPE " + cfg.Schema + "." + stateTypeName + " ADD VALUE IF NOT EXISTS " + Identifier.QuotePgString(s) + ";");
        }

        // Sort all migration statements ordinally for determinism.
        statements.Sort(System.StringComparer.Ordinal);

        return SqlWriter.Compose(
            SqlWriter.Header(key.Fqn, key.Version),
            SqlWriter.JoinBlocks(statements));
    }

    private static string RenderValueTypeDiff(
        ValueTypeDecl prev,
        ValueTypeDecl curr,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns)
    {
        string typeName = Identifier.ToSnakeCase(curr.Name);
        if (multiVersion) typeName += "_v" + key.Version.ToString(CultureInfo.InvariantCulture);
        string qual = cfg.Schema + "." + typeName;

        var statements = new List<string>();
        var prevFieldNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var f in prev.Fields) prevFieldNames.Add(f.Name);
        foreach (var f in curr.Fields)
        {
            if (prevFieldNames.Contains(f.Name)) continue;
            string colName = Identifier.ToSnakeCase(f.Name);
            string colType = TypeMapper.MapType(f.Type, cfg, multiVersionFqns, declToFile);
            statements.Add("ALTER TYPE " + qual + " ADD ATTRIBUTE IF NOT EXISTS " + colName + " " + colType + ";");
        }
        statements.Sort(System.StringComparer.Ordinal);

        return SqlWriter.Compose(
            SqlWriter.Header(key.Fqn, key.Version),
            SqlWriter.JoinBlocks(statements));
    }

    private static string RenderEnumDiff(
        EnumDecl prev,
        EnumDecl curr,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion)
    {
        string typeName = Identifier.ToSnakeCase(curr.Name);
        if (multiVersion) typeName += "_v" + key.Version.ToString(CultureInfo.InvariantCulture);
        string qual = cfg.Schema + "." + typeName;

        var statements = new List<string>();
        var prevVariants = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var v in prev.Variants) prevVariants.Add(v);
        foreach (var v in curr.Variants)
        {
            if (prevVariants.Contains(v)) continue;
            statements.Add("ALTER TYPE " + qual + " ADD VALUE IF NOT EXISTS " + Identifier.QuotePgString(v) + ";");
        }
        statements.Sort(System.StringComparer.Ordinal);

        return SqlWriter.Compose(
            SqlWriter.Header(key.Fqn, key.Version),
            SqlWriter.JoinBlocks(statements));
    }
}
