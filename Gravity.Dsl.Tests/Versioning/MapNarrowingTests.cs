using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Versioning;
using Xunit;

namespace Gravity.Dsl.Tests.Versioning;

/// <summary>
/// Narrowing behaviour for map types. A map-to-map transition narrows when its
/// key type changes or its value type narrows; an unchanged map does not narrow;
/// and a map ↔ non-map transition is treated as a contract change (narrowing).
/// </summary>
public sealed class MapNarrowingTests
{
    private static readonly SourceSpan Span = new("n.gravity", 1, 1, 0);

    private static TypeRef Prim(PrimitiveKind k, bool opt = false, bool arr = false)
        => new PrimitiveTypeRef(k, opt, arr, Span);

    private static TypeRef Map(TypeRef key, TypeRef value, bool opt = false, bool arr = false)
        => new MapTypeRef(key, value, opt, arr, Span);

    [Fact]
    public void IdenticalMap_DoesNotNarrow()
    {
        var prev = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String));
        var next = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String));
        Narrowing.IsNarrowing(prev, next).Should().BeFalse();
    }

    [Fact]
    public void KeyTypeChange_Narrows()
    {
        var prev = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String));
        var next = Map(Prim(PrimitiveKind.Int), Prim(PrimitiveKind.String));
        Narrowing.IsNarrowing(prev, next).Should().BeTrue();
    }

    [Fact]
    public void ValueNarrowing_Narrows()
    {
        // Decimal -> Int is a narrowing per the primitive table; applied to the value.
        var prev = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.Decimal));
        var next = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.Int));
        Narrowing.IsNarrowing(prev, next).Should().BeTrue();
    }

    [Fact]
    public void ValueWidening_DoesNotNarrow()
    {
        // Int -> Long is widening; the value transition does not narrow.
        var prev = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.Int));
        var next = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.Long));
        Narrowing.IsNarrowing(prev, next).Should().BeFalse();
    }

    [Fact]
    public void OptionalMapLost_Narrows()
    {
        var prev = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String), opt: true);
        var next = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String));
        Narrowing.IsNarrowing(prev, next).Should().BeTrue();
    }

    [Fact]
    public void MapToNonMap_Narrows()
    {
        var prev = Map(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String));
        var next = Prim(PrimitiveKind.String);
        Narrowing.IsNarrowing(prev, next).Should().BeTrue();
        Narrowing.IsNarrowing(next, prev).Should().BeTrue();
    }
}
