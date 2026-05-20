using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// FR-451 — acronym-aware snake_case mapping unit tests, plus
/// <c>IsValidPgIdentifier</c> coverage for PG001 / PG004.
/// </summary>
public sealed class IdentifierTests
{
    // The Identifier type is internal to the emitter assembly. Reflection-load
    // it so the test project does not need InternalsVisibleTo (the emitter
    // assembly's public surface is deliberately minimal).
    private static readonly System.Type IdentifierType =
        typeof(global::Gravity.Dsl.Emitter.PostgresDdl.PostgresDdlEmitter)
            .Assembly
            .GetType("Gravity.Dsl.Emitter.PostgresDdl.Render.Identifier", throwOnError: true)!;

    private static string ToSnake(string s)
        => (string)IdentifierType.GetMethod("ToSnakeCase", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { s })!;

    private static bool IsValid(string s)
        => (bool)IdentifierType.GetMethod("IsValidPgIdentifier", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { s })!;

    [Theory]
    [InlineData("firstName", "first_name")]
    [InlineData("FirstName", "first_name")]
    [InlineData("first_name", "first_name")]
    [InlineData("URL", "url")]
    [InlineData("Employee", "employee")]
    [InlineData("HTTPSResponse", "https_response")]
    [InlineData("contractType", "contract_type")]
    [InlineData("simple", "simple")]
    [InlineData("a", "a")]
    [InlineData("A", "a")]
    public void ToSnakeCase_FollowsAcronymAwareRule(string input, string expected)
    {
        ToSnake(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("hr_prod")]
    [InlineData("tenant_42")]
    [InlineData("_v1")]
    [InlineData("a")]
    public void IsValidPgIdentifier_AcceptsConformingNames(string s)
    {
        IsValid(s).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1bad")]
    [InlineData("Public")]      // starts with uppercase
    [InlineData("bad-name")]    // contains hyphen
    [InlineData("bad name")]    // contains space
    public void IsValidPgIdentifier_RejectsBadNames(string s)
    {
        IsValid(s).Should().BeFalse();
    }

    [Fact]
    public void IsValidPgIdentifier_RejectsOverLengthNames()
    {
        IsValid(new string('a', 63)).Should().BeTrue();
        IsValid(new string('a', 64)).Should().BeFalse();
    }
}
