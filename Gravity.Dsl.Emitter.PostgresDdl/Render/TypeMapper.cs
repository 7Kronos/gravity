using System;
using System.Collections.Generic;
using System.Globalization;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// FR-430..FR-435 — closed-form DSL → PostgreSQL column-type mapping. Pure
/// deterministic helpers; no clock, no I/O.
/// </summary>
internal static class TypeMapper
{
    /// <summary>FR-430 — primitive → PG column type.</summary>
    public static string MapPrimitive(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.String   => "TEXT",
        PrimitiveKind.Int      => "INTEGER",
        PrimitiveKind.Long     => "BIGINT",
        PrimitiveKind.Decimal  => "NUMERIC",
        PrimitiveKind.Boolean  => "BOOLEAN",
        PrimitiveKind.Date     => "DATE",
        PrimitiveKind.DateTime => "TIMESTAMPTZ",
        PrimitiveKind.Uuid     => "UUID",
        _ => throw new NotSupportedException("PrimitiveKind " + kind + " is not mapped to a PostgreSQL type."),
    };

    /// <summary>
    /// FR-430..FR-432, FR-435 — full DSL type → PG column type string. Returns
    /// the column type only (no <c>NOT NULL</c> / <c>DEFAULT</c> suffix — those
    /// belong to the caller's column-rendering layer per FR-431).
    /// </summary>
    public static string MapType(
        TypeRef typeRef,
        PostgresDdlEmitterConfig cfg,
        IReadOnlySet<string> multiVersionFqns,
        IReadOnlyDictionary<string, SourceFile> declToFile)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRef p:
            {
                string baseType = MapPrimitive(p.Kind);
                return p.IsArray ? baseType + "[]" : baseType;
            }
            case NamedTypeRef n:
            {
                string typeName = ResolveNamedTypeName(n, cfg, multiVersionFqns, declToFile);
                return n.IsArray ? typeName + "[]" : typeName;
            }
            default:
                throw new NotSupportedException("Unknown TypeRef shape " + typeRef.GetType().Name);
        }
    }

    /// <summary>
    /// Resolve the schema-qualified PG type name a <see cref="NamedTypeRef"/>
    /// refers to. Multi-version coexistence appends <c>_v&lt;N&gt;</c> to the
    /// type name when the referent FQN has more than one version in scope
    /// (FR-435).
    /// </summary>
    private static string ResolveNamedTypeName(
        NamedTypeRef n,
        PostgresDdlEmitterConfig cfg,
        IReadOnlySet<string> multiVersionFqns,
        IReadOnlyDictionary<string, SourceFile> declToFile)
    {
        string snake = Identifier.ToSnakeCase(n.Name);
        // Try to resolve the FQN by scanning declToFile. The same simple-name
        // may be present in multiple namespaces; we walk the keys looking for
        // a match. This is O(N) per reference but N is small (declaration
        // count). A tighter resolver would index by simple name; for v1 the
        // explicit walk is fine.
        string? fqn = null;
        foreach (var kvp in declToFile)
        {
            string key = kvp.Key;
            int lastDot = key.LastIndexOf('.');
            string simple = lastDot < 0 ? key : key.Substring(lastDot + 1);
            if (string.Equals(simple, n.Name, StringComparison.Ordinal))
            {
                fqn = key;
                break;
            }
        }
        if (fqn is not null && multiVersionFqns.Contains(fqn) && n.Version.HasValue)
        {
            return cfg.Schema + "." + snake + "_v" + n.Version.Value.ToString(CultureInfo.InvariantCulture);
        }
        return cfg.Schema + "." + snake;
    }

    /// <summary>
    /// Result of mapping a <see cref="RelationDecl"/> to a PG column.
    /// </summary>
    public sealed record RelationColumn(string ColumnName, string ColumnType, bool IsArrayMany);

    /// <summary>
    /// FR-433 / FR-434 — relation → column name + type + cardinality flag.
    /// Cardinality-one returns <c>(rel_name + "_id", "UUID", false)</c>;
    /// cardinality-many returns <c>(rel_name + "_ids", "UUID[]", true)</c>.
    /// Target identity primitive is assumed <c>UUID</c> (the documented norm).
    /// </summary>
    public static RelationColumn MapRelation(RelationDecl relation)
    {
        string baseName = Identifier.ToSnakeCase(relation.Name);
        if (relation.Cardinality == Cardinality.Many)
        {
            return new RelationColumn(baseName + "_ids", "UUID[]", IsArrayMany: true);
        }
        return new RelationColumn(baseName + "_id", "UUID", IsArrayMany: false);
    }

    /// <summary>Returns true when <paramref name="typeRef"/> carries the DSL <c>?</c> optional modifier.</summary>
    public static bool IsOptional(TypeRef typeRef) => typeRef switch
    {
        PrimitiveTypeRef p => p.IsOptional,
        NamedTypeRef n     => n.IsOptional,
        _ => false,
    };

    /// <summary>
    /// Resolve a foreign-key target table name for a cardinality-one relation.
    /// When the target FQN is multi-version (FR-425), the FK targets the
    /// version that matches the referrer's version (typical "same-vintage"
    /// migration), falling back to the highest version in scope when the
    /// matching version is absent. Single-version FQNs keep the unqualified
    /// table name.
    /// </summary>
    public static string ResolveFkTargetTable(
        RelationDecl relation,
        DeclKey referrerKey,
        IReadOnlyDictionary<string, SourceFile> declToFile,
        IReadOnlySet<string> multiVersionFqns,
        ResolvedModel model)
    {
        string baseSnake = Identifier.ToSnakeCase(relation.TargetEntity);
        string? fqn = ResolveTargetFqn(relation.TargetEntity, referrerKey.Fqn, declToFile);
        if (fqn is null || !multiVersionFqns.Contains(fqn))
        {
            return baseSnake;
        }

        // Pick the version matching the referrer; otherwise the highest
        // available version (the documented latest-version rule from Phase 8
        // FR-126 — the resolver's "max declared version" semantics).
        int targetVersion = -1;
        foreach (var kv in model.Declarations)
        {
            if (!string.Equals(kv.Key.Fqn, fqn, StringComparison.Ordinal)) continue;
            if (kv.Key.Version == referrerKey.Version)
            {
                targetVersion = kv.Key.Version;
                break;
            }
            if (kv.Key.Version > targetVersion) targetVersion = kv.Key.Version;
        }
        return targetVersion < 0
            ? baseSnake
            : baseSnake + "_v" + targetVersion.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Best-effort simple-name → FQN resolution. Prefers a target in the same
    /// namespace as the referrer; otherwise returns the lexicographically-first
    /// FQN with the matching simple name. The DSL grammar's import / resolver
    /// stage upstream already gates references — this helper is exclusively for
    /// the emitter's table-naming layer, which receives <see cref="RelationDecl"/>
    /// targets as bare identifiers.
    /// </summary>
    private static string? ResolveTargetFqn(
        string simpleName,
        string referrerFqn,
        IReadOnlyDictionary<string, SourceFile> declToFile)
    {
        string? referrerNs = null;
        int dot = referrerFqn.LastIndexOf('.');
        if (dot > 0) referrerNs = referrerFqn.Substring(0, dot);

        string? sameNsMatch = null;
        string? anyMatch = null;
        foreach (var kvp in declToFile)
        {
            string key = kvp.Key;
            int lastDot = key.LastIndexOf('.');
            string simple = lastDot < 0 ? key : key.Substring(lastDot + 1);
            if (!string.Equals(simple, simpleName, StringComparison.Ordinal)) continue;
            string ns = lastDot < 0 ? string.Empty : key.Substring(0, lastDot);
            if (referrerNs != null && string.Equals(ns, referrerNs, StringComparison.Ordinal))
            {
                sameNsMatch = key;
                break;
            }
            if (anyMatch == null || string.CompareOrdinal(key, anyMatch) < 0)
            {
                anyMatch = key;
            }
        }
        return sameNsMatch ?? anyMatch;
    }
}
