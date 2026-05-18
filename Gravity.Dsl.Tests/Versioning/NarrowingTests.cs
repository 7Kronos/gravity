using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Versioning;
using Xunit;

namespace Gravity.Dsl.Tests.Versioning;

/// <summary>
/// Phase 8 (T141 / FR-131) tests for the closed-form narrowing predicate.
/// Covers the 11 narrowing rows enumerated in the spec plus a sample of
/// widening rows. AC-8.2 (per-row narrowing detection in the validator) is
/// pinned end-to-end under <c>Val021Tests</c> against fixtures; this file
/// pins the predicate in isolation so a regression there shows up here too.
/// </summary>
public sealed class NarrowingTests
{
    private static readonly SourceSpan Span = new("n.gravity", 1, 1, 0);

    private static TypeRef Prim(PrimitiveKind k, bool opt = false, bool arr = false)
        => new PrimitiveTypeRef(k, opt, arr, Span);

    private static TypeRef Named(string name, int? version = null, bool opt = false, bool arr = false)
        => new NamedTypeRef(name, opt, arr, Span, version);

    // ---- structural narrowings ----

    [Fact]
    public void OptionalLost_Narrows()
        => Narrowing.IsNarrowing(Prim(PrimitiveKind.String, opt: true), Prim(PrimitiveKind.String)).Should().BeTrue();

    [Fact]
    public void ArrayLost_Narrows()
        => Narrowing.IsNarrowing(Prim(PrimitiveKind.Int, arr: true), Prim(PrimitiveKind.Int)).Should().BeTrue();

    // ---- primitive narrowing rows (9 explicit) ----

    [Theory]
    [InlineData(PrimitiveKind.Decimal, PrimitiveKind.Int)]
    [InlineData(PrimitiveKind.Decimal, PrimitiveKind.Long)]
    [InlineData(PrimitiveKind.Long, PrimitiveKind.Int)]
    [InlineData(PrimitiveKind.DateTime, PrimitiveKind.Date)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.Int)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.Long)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.Decimal)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.Boolean)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.Uuid)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.Date)]
    [InlineData(PrimitiveKind.String, PrimitiveKind.DateTime)]
    public void PrimitiveTable_NarrowingRows(PrimitiveKind prev, PrimitiveKind next)
        => Narrowing.IsNarrowing(Prim(prev), Prim(next)).Should().BeTrue();

    // ---- widening / non-narrowing rows (sample) ----

    [Theory]
    [InlineData(PrimitiveKind.Int, PrimitiveKind.Long)]
    [InlineData(PrimitiveKind.Int, PrimitiveKind.Decimal)]
    [InlineData(PrimitiveKind.Long, PrimitiveKind.Decimal)]
    [InlineData(PrimitiveKind.Date, PrimitiveKind.DateTime)]
    [InlineData(PrimitiveKind.Uuid, PrimitiveKind.String)]
    [InlineData(PrimitiveKind.Boolean, PrimitiveKind.String)]
    [InlineData(PrimitiveKind.Int, PrimitiveKind.String)]
    public void PrimitiveTable_WideningRows(PrimitiveKind prev, PrimitiveKind next)
        => Narrowing.IsNarrowing(Prim(prev), Prim(next)).Should().BeFalse();

    // ---- same-kind ----

    [Fact]
    public void SamePrimitive_NotNarrowing()
        => Narrowing.IsNarrowing(Prim(PrimitiveKind.String), Prim(PrimitiveKind.String)).Should().BeFalse();

    // ---- named-named ----

    [Fact]
    public void NamedNamed_VersionDecrease_Narrows()
        => Narrowing.IsNarrowing(Named("Money", 2), Named("Money", 1)).Should().BeTrue();

    [Fact]
    public void NamedNamed_VersionIncrease_NotNarrowing()
        => Narrowing.IsNarrowing(Named("Money", 1), Named("Money", 2)).Should().BeFalse();

    [Fact]
    public void NamedNamed_Rename_NotNarrowing()
        => Narrowing.IsNarrowing(Named("Foo"), Named("Bar")).Should().BeFalse();

    [Fact]
    public void NamedNamed_SameNameNoVersion_NotNarrowing()
        => Narrowing.IsNarrowing(Named("Money"), Named("Money")).Should().BeFalse();

    // ---- cross-kind ----

    [Fact]
    public void CrossKind_PrimitiveToNamed_Narrows()
        => Narrowing.IsNarrowing(Prim(PrimitiveKind.String), Named("Money", 1)).Should().BeTrue();

    [Fact]
    public void CrossKind_NamedToPrimitive_Narrows()
        => Narrowing.IsNarrowing(Named("Money", 1), Prim(PrimitiveKind.String)).Should().BeTrue();
}
