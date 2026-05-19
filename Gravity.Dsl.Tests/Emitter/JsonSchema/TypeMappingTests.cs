using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.JsonSchema;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.5 + AC-4.6 — type mapping table and modifier-combination assertions.
/// Builds a small fixture entity per primitive × modifier combo, runs the
/// emitter, and inspects the produced entity bundle's <c>properties.field</c>.
/// </summary>
public sealed class TypeMappingTests
{
    private static readonly System.Text.Json.JsonDocumentOptions DocOpts = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    };

    private static System.Text.Json.JsonElement RunAndExtractFieldFragment(string fixtureSrc, string propertyName)
    {
        var parsed = Parser.Parse("Fixture.gravity", fixtureSrc);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull();

        var emitter = new JsonSchemaEmitter();
        var config = new EmitterConfig(
            TargetName: "json-schema", Enabled: true, Output: "out",
            Values: ImmutableSortedDictionary<string, object>.Empty.Add("output", "out"));
        var sink = new BufferedEmitterOutput();
        var result = emitter.Emit(resolve.Model!, config, sink);
        result.Diagnostics.Should().BeEmpty(because: "happy-path fixture must not raise diagnostics");

        var snap = sink.Snapshot();
        var bundle = snap.Single(kv => kv.Key.EndsWith("F.json")).Value;
        using var doc = System.Text.Json.JsonDocument.Parse(bundle, DocOpts);
        return doc.RootElement
            .GetProperty("properties")
            .GetProperty(propertyName)
            .Clone();
    }

    // Build a minimal one-property entity fixture. The entity must declare
    // identity + at least one lifecycle state per FR-021 / FR-024.
    private static string FixtureWith(string propertyDecl) =>
$@"entity F version 1 {{
  identity id: UUID;
  properties {{
    {propertyDecl};
  }}
  lifecycle {{
    states {{ Active; }}
    transitions {{}}
  }}
  events {{}}
  commands {{}}
}}";

    public static IEnumerable<object[]> PrimitiveExpectations()
    {
        // (decl-fragment, expected-type-json-fragment, requiredInBundle)
        // We test all 8 primitives in non-array, non-optional form.
        yield return new object[] { "x: String", "{\"type\":\"string\"}" };
        yield return new object[] { "x: Int",
            "{\"type\":\"integer\",\"minimum\":-2147483648,\"maximum\":2147483647}" };
        yield return new object[] { "x: Long",
            "{\"type\":\"integer\",\"minimum\":-9223372036854775808,\"maximum\":9223372036854775807}" };
        yield return new object[] { "x: Decimal",
            "{\"type\":\"string\",\"format\":\"decimal\"}" };
        yield return new object[] { "x: Boolean", "{\"type\":\"boolean\"}" };
        yield return new object[] { "x: Date",
            "{\"type\":\"string\",\"format\":\"date\"}" };
        yield return new object[] { "x: DateTime",
            "{\"type\":\"string\",\"format\":\"date-time\"}" };
        yield return new object[] { "x: UUID",
            "{\"type\":\"string\",\"format\":\"uuid\"}" };
    }

    [Theory]
    [MemberData(nameof(PrimitiveExpectations))]
    public void Primitives_MapPerFR330Table(string declFragment, string expectedJson)
    {
        var actual = RunAndExtractFieldFragment(FixtureWith(declFragment), "x");
        // Compare semantic JSON equality.
        using var expectedDoc = System.Text.Json.JsonDocument.Parse(expectedJson);
        JsonElementEqualOrdered(actual, expectedDoc.RootElement).Should().BeTrue(
            because: declFragment + " should map to " + expectedJson + " but got " + actual.GetRawText());
    }

    [Fact]
    public void Long_PinsInt64Bounds_NotInt32()
    {
        // AC-4.5: regression that overflows to int32 bounds must fail.
        var actual = RunAndExtractFieldFragment(FixtureWith("x: Long"), "x");
        actual.GetProperty("minimum").GetInt64().Should().Be(long.MinValue);
        actual.GetProperty("maximum").GetInt64().Should().Be(long.MaxValue);
    }

    [Fact]
    public void Decimal_IsStringEncoded()
    {
        // FR-330 row 4 rationale: JSON's number is IEEE-754 double which cannot
        // losslessly represent regulatory decimals.
        var actual = RunAndExtractFieldFragment(FixtureWith("x: Decimal"), "x");
        actual.GetProperty("type").GetString().Should().Be("string");
        actual.GetProperty("format").GetString().Should().Be("decimal");
    }

    [Fact]
    public void Optional_DoesNotAlterFragment_AndExcludesFromRequired()
    {
        // FR-331: T? produces identical fragment; the property name is omitted from `required`.
        var src = FixtureWith("opt: String?");
        var parsed = Parser.Parse("Fixture.gravity", src);
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        var emitter = new JsonSchemaEmitter();
        var sink = new BufferedEmitterOutput();
        var cfg = new EmitterConfig("json-schema", true, "out",
            ImmutableSortedDictionary<string, object>.Empty.Add("output", "out"));
        emitter.Emit(resolve.Model!, cfg, sink);
        var doc = System.Text.Json.JsonDocument.Parse(sink.Snapshot().Single().Value);
        var root = doc.RootElement;
        root.GetProperty("properties").GetProperty("opt").GetProperty("type").GetString().Should().Be("string");
        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray();
        required.Should().NotContain("opt", because: "FR-331: optionals are absent from required");
        required.Should().Contain("id", because: "identity is always required");
        required.Should().Contain("state", because: "the reserved state slot is always required");
    }

    [Fact]
    public void Array_WrapsItems()
    {
        var actual = RunAndExtractFieldFragment(FixtureWith("xs: String[]"), "xs");
        actual.GetProperty("type").GetString().Should().Be("array");
        actual.GetProperty("items").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void OptionalArray_DropsInnerOptional_FR332Asymmetry()
    {
        // FR-332: String?[] and String[] produce identical `items` fragments;
        // the inner `?` is dropped on items with no diagnostic (JSON arrays
        // have no absent slots).
        var withInnerOpt = RunAndExtractFieldFragment(FixtureWith("xs: String?[]"), "xs");
        var plainArray = RunAndExtractFieldFragment(FixtureWith("xs: String[]"), "xs");
        withInnerOpt.GetRawText().Should().Be(plainArray.GetRawText(),
            because: "FR-332 documented asymmetry: String?[] and String[] produce identical fragments");
    }

    private static bool JsonElementEqualOrdered(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToArray();
                var bProps = b.EnumerateObject().ToArray();
                if (aProps.Length != bProps.Length) return false;
                // Compare as set of name→value pairs (order-insensitive)
                var aDict = aProps.ToDictionary(p => p.Name, p => p.Value);
                foreach (var bp in bProps)
                {
                    if (!aDict.TryGetValue(bp.Name, out var av)) return false;
                    if (!JsonElementEqualOrdered(av, bp.Value)) return false;
                }
                return true;
            case System.Text.Json.JsonValueKind.Array:
                var arrA = a.EnumerateArray().ToArray();
                var arrB = b.EnumerateArray().ToArray();
                if (arrA.Length != arrB.Length) return false;
                for (int i = 0; i < arrA.Length; i++)
                {
                    if (!JsonElementEqualOrdered(arrA[i], arrB[i])) return false;
                }
                return true;
            case System.Text.Json.JsonValueKind.String:
                return a.GetString() == b.GetString();
            case System.Text.Json.JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
            case System.Text.Json.JsonValueKind.Null:
                return true;
            default:
                return a.GetRawText() == b.GetRawText();
        }
    }
}
