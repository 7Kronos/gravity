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
/// FR-420 / FR-423 / FR-424 / FR-433 / FR-434 / FR-453 — composes the full
/// per-entity baseline file: header → lifecycle state enum → CREATE TABLE →
/// foreign-key constraint blocks (sorted ordinally) → CREATE INDEX blocks
/// (sorted ordinally).
/// </summary>
internal static class EntityTableRenderer
{
    /// <summary>
    /// Compose the snake_case table name for <paramref name="entity"/>,
    /// applying the <c>_v&lt;N&gt;</c> suffix when the FQN appears at more
    /// than one version in scope (FR-425).
    /// </summary>
    public static string TableName(EntityDecl entity, int version, bool multiVersion)
    {
        string baseName = Identifier.ToSnakeCase(entity.Name);
        return multiVersion ? baseName + "_v" + version.ToString(CultureInfo.InvariantCulture) : baseName;
    }

    public static string? Render(
        EntityDecl entity,
        ResolvedModel model,
        DeclKey key,
        PostgresDdlEmitterConfig cfg,
        bool multiVersion,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        // PG002 — user property named `state` collides with the reserved
        // lifecycle-state column. Skip the entity entirely on collision.
        foreach (var p in entity.Properties)
        {
            string mapped = Identifier.ToSnakeCase(p.Name);
            if (string.Equals(mapped, "state", StringComparison.Ordinal))
            {
                diags.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    PgRuleIds.Pg002,
                    "entity '" + key.Fqn
                        + "' declares a property '" + p.Name
                        + "' that maps to column 'state' and collides with the reserved lifecycle-state column",
                    p.Span));
                return null;
            }
        }

        string tableName = TableName(entity, key.Version, multiVersion);
        string identityColName = Identifier.ToSnakeCase(entity.Identity.FieldName);
        string identityColType = TypeMapper.MapType(entity.Identity.Type, cfg, multiVersionFqns, declToFile);

        // ---- Lifecycle state enum (FR-424).
        string stateEnumBlock = LifecycleStateRenderer.Render(entity, cfg, tableName);
        string stateTypeName = LifecycleStateRenderer.TypeName(tableName);

        // ---- Column list (FR-423).
        var columnLines = new List<string>();
        var foreignKeys = new List<string>();
        var indexes = new List<(string Name, string Statement)>();

        // (1) Identity column.
        columnLines.Add(identityColName + " " + identityColType + " NOT NULL");
        // (2) State column.
        columnLines.Add("state " + cfg.Schema + "." + stateTypeName + " NOT NULL");
        // (3) Property columns (DSL declaration order).
        foreach (var p in entity.Properties)
        {
            var folded = AnnotationFolder.Fold(p.Annotations, p.Name, key.Fqn, diags);
            string colName = folded.OverrideColumnName ?? Identifier.ToSnakeCase(p.Name);
            string colType = TypeMapper.MapType(p.Type, cfg, multiVersionFqns, declToFile);
            var line = new StringBuilder();
            line.Append(colName).Append(' ').Append(colType);
            if (!TypeMapper.IsOptional(p.Type)) line.Append(" NOT NULL");
            if (folded.MarkUnique) line.Append(" UNIQUE");
            if (folded.DefaultExpression is not null) line.Append(" DEFAULT ").Append(folded.DefaultExpression);
            columnLines.Add(line.ToString());

            if (folded.MarkIndexed)
            {
                string ixName = "ix_" + tableName + "_" + colName;
                indexes.Add((ixName,
                    "CREATE INDEX IF NOT EXISTS " + ixName + " ON " + cfg.Schema + "." + tableName + "(" + colName + ");"));
            }
        }
        // (4) Relation columns (DSL declaration order).
        foreach (var r in entity.Relations)
        {
            var rc = TypeMapper.MapRelation(r);
            var line = new StringBuilder();
            line.Append(rc.ColumnName).Append(' ').Append(rc.ColumnType);
            if (!r.IsOptional)
            {
                line.Append(" NOT NULL");
                if (rc.IsArrayMany) line.Append(" DEFAULT '{}'::").Append(rc.ColumnType);
            }
            columnLines.Add(line.ToString());

            // FR-433 — cardinality-one relations get a FK constraint + btree index.
            // FR-434 — cardinality-many relations skip the FK (PG doesn't enforce
            // FKs on array elements) and get a GIN index instead.
            string ixName = "ix_" + tableName + "_" + rc.ColumnName;
            if (rc.IsArrayMany)
            {
                indexes.Add((ixName,
                    "CREATE INDEX IF NOT EXISTS " + ixName + " ON " + cfg.Schema + "." + tableName
                    + " USING GIN (" + rc.ColumnName + ");"));
            }
            else
            {
                string targetTable = TypeMapper.ResolveFkTargetTable(r, key, declToFile, multiVersionFqns, model);
                string targetIdCol = "id"; // documented norm: identity field is `id` of type UUID.
                string fkName = "fk_" + tableName + "_" + rc.ColumnName;
                string fkInner =
                    "ALTER TABLE " + cfg.Schema + "." + tableName
                    + " ADD CONSTRAINT " + fkName
                    + " FOREIGN KEY (" + rc.ColumnName + ")"
                    + " REFERENCES " + cfg.Schema + "." + targetTable + "(" + targetIdCol + ");";
                foreignKeys.Add(SqlWriter.WrapIdempotent(fkInner));
                indexes.Add((ixName,
                    "CREATE INDEX IF NOT EXISTS " + ixName + " ON " + cfg.Schema + "." + tableName
                    + "(" + rc.ColumnName + ");"));
            }
        }

        // (5) Assemble CREATE TABLE.
        var createTable = new StringBuilder();
        createTable.Append("CREATE TABLE IF NOT EXISTS ").Append(cfg.Schema).Append('.').Append(tableName).Append(" (").Append(SqlWriter.Lf);
        for (int i = 0; i < columnLines.Count; i++)
        {
            createTable.Append(SqlWriter.Indent).Append(columnLines[i]).Append(',').Append(SqlWriter.Lf);
        }
        createTable.Append(SqlWriter.Indent).Append("PRIMARY KEY (").Append(identityColName).Append(')').Append(SqlWriter.Lf);
        createTable.Append(");");

        // (6) Sort FK blocks ordinally by constraint name (FR-453).
        foreignKeys.Sort(System.StringComparer.Ordinal);

        // (7) Sort indexes ordinally by name (FR-453). Deduplicate by name —
        // an @postgres(index: true) on a relation column would otherwise create
        // a duplicate index alongside the cardinality-derived one.
        var seenIndexNames = new HashSet<string>(System.StringComparer.Ordinal);
        var dedup = new List<(string, string)>();
        foreach (var ix in indexes)
        {
            if (seenIndexNames.Add(ix.Name)) dedup.Add(ix);
        }
        dedup.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.Item1, b.Item1));
        var indexBlock = new StringBuilder();
        for (int i = 0; i < dedup.Count; i++)
        {
            if (i > 0) indexBlock.Append(SqlWriter.Lf);
            indexBlock.Append(dedup[i].Item2);
        }

        return SqlWriter.Compose(
            SqlWriter.Header(key.Fqn, key.Version),
            stateEnumBlock,
            createTable.ToString(),
            SqlWriter.JoinBlocks(foreignKeys),
            indexBlock.ToString());
    }
}
