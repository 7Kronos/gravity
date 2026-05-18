using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Versioning;

/// <summary>
/// Closed-form narrowing predicate over the AST <see cref="TypeRef"/> surface
/// (Phase 8, FR-131). Used by <c>VAL021</c> on surviving fields and by
/// <c>VAL026</c> sub-cause (c) on surviving command arguments.
/// <para>
/// The primitive table is closed: only the explicit pairs listed below narrow;
/// every other primitive transition (including <c>Int</c>→<c>Long</c>,
/// <c>UUID</c>→<c>String</c>, <c>Boolean</c>→<c>String</c>, etc.) is widening
/// or sideways and returns <c>false</c>.
/// </para>
/// </summary>
internal static class Narrowing
{
    /// <summary>
    /// Return <c>true</c> iff transitioning a field's type from <paramref name="prev"/>
    /// to <paramref name="next"/> is a breaking narrowing. Optionality loss, array-ness
    /// loss, primitive narrowings from the FR-131 table, named-named version decreases,
    /// and cross-kind transitions (primitive ↔ named) are all narrowing. A pure rename
    /// (same kind, different <see cref="NamedTypeRef.Name"/>) returns <c>false</c>
    /// because the rename is reported as <c>VAL020</c> add+remove, not <c>VAL021</c>.
    /// </summary>
    public static bool IsNarrowing(TypeRef prev, TypeRef next)
    {
        // (1) Optionality lost: T? -> T narrows.
        if (Opt(prev) && !Opt(next)) return true;
        // (2) Array-ness lost: T[] -> T narrows.
        if (Arr(prev) && !Arr(next)) return true;

        // (3) Same-kind primitive: apply the closed-form table.
        if (prev is PrimitiveTypeRef pp && next is PrimitiveTypeRef pn)
        {
            return IsPrimitiveNarrowing(pp.Kind, pn.Kind);
        }

        // (4) Named-named: rename suppression + version-decrease narrowing.
        if (prev is NamedTypeRef np && next is NamedTypeRef nn)
        {
            if (!string.Equals(np.Name, nn.Name, System.StringComparison.Ordinal))
            {
                // Different names are renames; VAL020 add+remove path, not VAL021.
                return false;
            }
            // Same name; treat null Version as int.MaxValue ("max-of-imports unknown
            // upper bound"). A transition from m to n narrows iff m > n.
            int prevV = np.Version ?? int.MaxValue;
            int nextV = nn.Version ?? int.MaxValue;
            return prevV > nextV;
        }

        // (5) Cross-kind (Primitive <-> Named): the safest assumption per Principle IV
        // is "this is a contract change", so we treat both directions as narrowing.
        return (prev is PrimitiveTypeRef && next is NamedTypeRef)
            || (prev is NamedTypeRef && next is PrimitiveTypeRef);
    }

    private static bool Opt(TypeRef t) => t switch
    {
        PrimitiveTypeRef p => p.IsOptional,
        NamedTypeRef n => n.IsOptional,
        _ => false
    };

    private static bool Arr(TypeRef t) => t switch
    {
        PrimitiveTypeRef p => p.IsArray,
        NamedTypeRef n => n.IsArray,
        _ => false
    };

    private static bool IsPrimitiveNarrowing(PrimitiveKind prev, PrimitiveKind next)
    {
        if (prev == next) return false;
        return (prev, next) switch
        {
            (PrimitiveKind.Decimal, PrimitiveKind.Int) => true,
            (PrimitiveKind.Decimal, PrimitiveKind.Long) => true,
            (PrimitiveKind.Long, PrimitiveKind.Int) => true,
            (PrimitiveKind.DateTime, PrimitiveKind.Date) => true,
            (PrimitiveKind.String, PrimitiveKind.Int) => true,
            (PrimitiveKind.String, PrimitiveKind.Long) => true,
            (PrimitiveKind.String, PrimitiveKind.Decimal) => true,
            (PrimitiveKind.String, PrimitiveKind.Boolean) => true,
            (PrimitiveKind.String, PrimitiveKind.Uuid) => true,
            (PrimitiveKind.String, PrimitiveKind.Date) => true,
            (PrimitiveKind.String, PrimitiveKind.DateTime) => true,
            _ => false
        };
    }
}
