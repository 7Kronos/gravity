using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Validation;
using Xunit;

namespace Gravity.Dsl.Tests.Validation;

/// <summary>
/// VAL031 — a map key type must be a non-optional, non-array scalar primitive.
/// Map keys become JSON object property names in the JSON Schema target, so a
/// non-scalar key cannot be string-serialized deterministically.
/// </summary>
public sealed class MapKeyValidationTests
{
    private static readonly HashSet<string> Claimed = new(System.StringComparer.Ordinal) { "csharp" };

    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var parsed = Parser.Parse("v.gravity", source);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture should parse: " + source);
        var resolve = Resolver.Resolve(new[] { parsed.File! }, System.IO.Directory.GetCurrentDirectory());
        resolve.Model.Should().NotBeNull(because: "fixture should resolve: " + source);
        return Validator.Validate(resolve.Model!, Claimed, default(System.DateOnly));
    }

    [Fact]
    public void ScalarKey_IsValid()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    properties { external_ids: Map<String, String>; }
}";
        Run(src).Should().NotContain(d => d.RuleId == "VAL031");
    }

    [Fact]
    public void IntKey_IsValid()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    properties { lookup: Map<Int, String>; }
}";
        Run(src).Should().NotContain(d => d.RuleId == "VAL031");
    }

    [Fact]
    public void OptionalKey_IsRejected()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    properties { bad: Map<String?, String>; }
}";
        Run(src).Should().Contain(d => d.RuleId == "VAL031" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NamedTypeKey_IsRejected()
    {
        // A value type used as a map key is not a scalar primitive.
        var src = @"
type Money { amount: Decimal; }
entity X version 1 {
    identity id: UUID;
    properties { bad: Map<Money, String>; }
}";
        Run(src).Should().Contain(d => d.RuleId == "VAL031" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NestedMapKey_IsCheckedRecursively()
    {
        // The inner map's key is an array (non-scalar) and must be flagged.
        var src = @"
entity X version 1 {
    identity id: UUID;
    properties { bad: Map<String, Map<String[], String>>; }
}";
        Run(src).Should().Contain(d => d.RuleId == "VAL031");
    }
}
