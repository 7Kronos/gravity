using System;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter;

public sealed class SemanticVersionRangeTests
{
    [Theory]
    [InlineData(">=1.0.0 <2.0.0", "1.0.0", true)]
    [InlineData(">=1.0.0 <2.0.0", "1.5.3", true)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
    [InlineData(">=1.0.0 <2.0.0", "0.9.9", false)]
    [InlineData(">=1.0.0", "10.0.0", true)]
    [InlineData(">1.0.0", "1.0.0", false)]
    [InlineData(">1.0.0", "1.0.1", true)]
    [InlineData("<=1.0.0", "1.0.0", true)]
    [InlineData("<=1.0.0", "1.0.1", false)]
    [InlineData("=1.0.0", "1.0.0", true)]
    [InlineData("=1.0.0", "1.0.1", false)]
    [InlineData("1.0.0", "1.0.0", true)] // No prefix == equality.
    public void Range_AcceptsExpectedVersions(string expr, string version, bool expected)
    {
        var range = SemanticVersionRange.Parse(expr);
        range.Satisfies(version).Should().Be(expected);
    }

    [Fact]
    public void Parse_AcceptsTwoOrThreeComponentVersions()
    {
        SemanticVersionRange.Parse(">=1.0").Satisfies("1.0.0").Should().BeTrue();
        SemanticVersionRange.Parse(">=1").Satisfies("1.0.0").Should().BeTrue();
    }

    [Fact]
    public void Parse_EmptyExpression_Throws()
    {
        Action act = () => SemanticVersionRange.Parse("");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_NegativeComponent_Throws()
    {
        Action act = () => SemanticVersionRange.Parse(">=-1.0.0");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToString_PreservesExpression()
    {
        var expr = ">=1.0.0 <2.0.0";
        SemanticVersionRange.Parse(expr).ToString().Should().Be(expr);
    }
}
