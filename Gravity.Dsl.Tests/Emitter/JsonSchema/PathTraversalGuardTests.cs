using System;
using FluentAssertions;
using Gravity.Dsl.Emitter.JsonSchema;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// Defense-in-depth guard: namespace segments that would compose into
/// path-traversal artifacts (<c>..</c>, embedded <c>/</c>, embedded <c>\</c>,
/// empty) must be rejected by the emitter before they reach
/// <see cref="IEmitterOutput.WriteFile"/>. The grammar identifier rule
/// already forbids them at parse time; this layer catches synthetic
/// <c>ResolvedModel</c> inputs that bypass the parser.
/// </summary>
public sealed class PathTraversalGuardTests
{
    [Theory]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    public void ValidateNamespaceSegment_RejectsUnsafeSegment(string segment)
    {
        Action act = () => JsonSchemaEmitter.ValidateNamespaceSegment(segment);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a safe path component*");
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("Foo_bar")]
    [InlineData("Bar123")]
    public void ValidateNamespaceSegment_AcceptsSafeSegment(string segment)
    {
        Action act = () => JsonSchemaEmitter.ValidateNamespaceSegment(segment);
        act.Should().NotThrow();
    }
}
