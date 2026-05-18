using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.CSharp;

/// <summary>
/// Maps Gravity DSL <see cref="TypeRef"/> values to their C# surface form. The
/// mapping is fixed per FR-010: each primitive has exactly one C# spelling, the
/// optional marker (<c>?</c>) becomes a nullable annotation, and the array marker
/// (<c>[]</c>) becomes <see cref="System.Collections.Immutable.ImmutableArray{T}"/>.
/// Named references emit verbatim as the simple C# type name.
/// </summary>
internal static class TypeMapper
{
    /// <summary>Render <paramref name="typeRef"/> as a C# type expression.</summary>
    public static string Render(TypeRef typeRef)
    {
        var (core, isOptional, isArray) = Decompose(typeRef);
        // Per FR-011 the parser preserves both bools but not the textual order;
        // we emit `ImmutableArray<T>?` for Optional+Array (mirrors the canonical
        // SourceWriter ordering, which prints `?` before `[]`).
        if (isArray)
        {
            return isOptional ? "ImmutableArray<" + core + ">?" : "ImmutableArray<" + core + ">";
        }
        return isOptional ? core + "?" : core;
    }

    private static (string Core, bool IsOptional, bool IsArray) Decompose(TypeRef tr) => tr switch
    {
        PrimitiveTypeRef p => (PrimitiveName(p.Kind), p.IsOptional, p.IsArray),
        NamedTypeRef n => (n.Name, n.IsOptional, n.IsArray),
        _ => throw new System.InvalidOperationException(
            "unsupported TypeRef " + tr.GetType().FullName)
    };

    private static string PrimitiveName(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.String => "string",
        PrimitiveKind.Int => "int",
        PrimitiveKind.Long => "long",
        PrimitiveKind.Decimal => "decimal",
        PrimitiveKind.Boolean => "bool",
        PrimitiveKind.Date => "DateOnly",
        PrimitiveKind.DateTime => "DateTime",
        PrimitiveKind.Uuid => "Guid",
        _ => throw new System.InvalidOperationException(
            "unknown PrimitiveKind " + kind.ToString())
    };
}
