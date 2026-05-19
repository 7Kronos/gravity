using System.Collections.Immutable;
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
/// AC-4.8 + AC-4.12 — claimed-keyword folding (positive cases) and per-key
/// value-type validation (negative cases). Covers JS001 (unknown key,
/// value-type mismatch, multipleOf:0) and JS004 (unknown format string).
/// </summary>
public sealed class AnnotationFoldingTests
{
    private static (ImmutableArray<Diagnostic> Diags, System.Text.Json.JsonElement Fragment) RunAndExtract(
        string fixtureSrc, string propertyName)
    {
        var parsed = Parser.Parse("Fixture.gravity", fixtureSrc);
        parsed.Diagnostics.Should().BeEmpty();
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull();
        var emitter = new JsonSchemaEmitter();
        var cfg = new EmitterConfig("json-schema", true, "out",
            ImmutableSortedDictionary<string, object>.Empty.Add("output", "out"));
        var sink = new BufferedEmitterOutput();
        var result = emitter.Emit(resolve.Model!, cfg, sink);
        var bundle = sink.Snapshot().Single().Value;
        using var doc = System.Text.Json.JsonDocument.Parse(bundle);
        var frag = doc.RootElement.GetProperty("properties").GetProperty(propertyName).Clone();
        return (result.Diagnostics.ToImmutableArray(), frag);
    }

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

    [Fact]
    public void Positive_AllClaimedStringKeysFoldOnto_StringProperty()
    {
        var src = FixtureWith(
            @"email: String @json_schema(format: ""email"", pattern: ""^.+@.+$"", description: ""Primary email"", minLength: 3, maxLength: 254)");
        var (diags, frag) = RunAndExtract(src, "email");
        diags.Should().BeEmpty();
        frag.GetProperty("type").GetString().Should().Be("string");
        frag.GetProperty("format").GetString().Should().Be("email");
        frag.GetProperty("pattern").GetString().Should().Be("^.+@.+$");
        frag.GetProperty("description").GetString().Should().Be("Primary email");
        frag.GetProperty("minLength").GetInt64().Should().Be(3);
        frag.GetProperty("maxLength").GetInt64().Should().Be(254);
    }

    [Fact]
    public void Positive_DecimalProperty_FoldsMultipleOf()
    {
        var src = FixtureWith(@"amount: Decimal @json_schema(multipleOf: 0.01)");
        var (diags, frag) = RunAndExtract(src, "amount");
        diags.Should().BeEmpty();
        frag.GetProperty("type").GetString().Should().Be("string");
        frag.GetProperty("format").GetString().Should().Be("decimal");
        frag.GetProperty("multipleOf").GetDecimal().Should().Be(0.01m);
    }

    [Fact]
    public void Negative_FormatNonString_Js001()
    {
        var src = FixtureWith(@"email: String @json_schema(format: 42)");
        var (diags, frag) = RunAndExtract(src, "email");
        diags.Should().ContainSingle(d => d.RuleId == JsonRuleIds.Js001 && d.Severity == DiagnosticSeverity.Error)
            .Which.Message.Should().Contain("format").And.Contain("string").And.Contain("42");
        frag.TryGetProperty("format", out _).Should().BeFalse(
            because: "rejected annotation must not fold onto the fragment");
    }

    [Fact]
    public void Negative_MinLengthString_Js001()
    {
        var src = FixtureWith(@"name: String @json_schema(minLength: ""ten"")");
        var (diags, _) = RunAndExtract(src, "name");
        diags.Should().ContainSingle(d => d.RuleId == JsonRuleIds.Js001 && d.Severity == DiagnosticSeverity.Error)
            .Which.Message.Should().Contain("minLength").And.Contain("non-negative integer");
    }

    [Fact]
    public void Negative_MultipleOfZero_Js001()
    {
        var src = FixtureWith(@"x: Decimal @json_schema(multipleOf: 0)");
        var (diags, _) = RunAndExtract(src, "x");
        diags.Should().ContainSingle(d => d.RuleId == JsonRuleIds.Js001 && d.Severity == DiagnosticSeverity.Error)
            .Which.Message.Should().Contain("multipleOf").And.Contain("non-zero");
    }

    [Fact]
    public void Negative_UnknownClaimedKey_Js001()
    {
        var src = FixtureWith(@"x: String @json_schema(unknown_key: ""x"")");
        var (diags, _) = RunAndExtract(src, "x");
        diags.Should().ContainSingle(d => d.RuleId == JsonRuleIds.Js001 && d.Severity == DiagnosticSeverity.Error)
            .Which.Message.Should().Contain("unknown_key").And.Contain("json_schema");
    }

    [Fact]
    public void Js004_UnknownFormat_NonBlocking_PassesThroughVerbatim()
    {
        var src = FixtureWith(@"x: String @json_schema(format: ""duration"")");
        var (diags, frag) = RunAndExtract(src, "x");
        diags.Should().ContainSingle(d => d.RuleId == JsonRuleIds.Js004 && d.Severity == DiagnosticSeverity.Warning);
        diags.Should().NotContain(d => d.RuleId == JsonRuleIds.Js001);
        frag.GetProperty("format").GetString().Should().Be("duration");
    }

    [Fact]
    public void KnownFormat_DoesNotEmitJs004()
    {
        var src = FixtureWith(@"x: String @json_schema(format: ""ipv6"")");
        var (diags, frag) = RunAndExtract(src, "x");
        diags.Should().NotContain(d => d.RuleId == JsonRuleIds.Js004);
        diags.Should().NotContain(d => d.RuleId == JsonRuleIds.Js001);
        frag.GetProperty("format").GetString().Should().Be("ipv6");
    }

    [Fact]
    public void ArrayProperty_ItemLevelKeys_FoldOntoItemsNotWrapper()
    {
        // FR-332: when the outer fragment is { "type": "array", "items": ... },
        // item-level constraint keywords (pattern, minLength, etc.) belong on
        // the items schema. description/examples stay on the array wrapper.
        var src = FixtureWith(
            @"tags: String[] @json_schema(minLength: 2, maxLength: 32, pattern: ""^[a-z]+$"", description: ""tag list"")");
        var (diags, frag) = RunAndExtract(src, "tags");
        diags.Should().BeEmpty();
        frag.GetProperty("type").GetString().Should().Be("array");
        // Array-level metadata stays on the wrapper.
        frag.GetProperty("description").GetString().Should().Be("tag list");
        frag.TryGetProperty("minLength", out _).Should().BeFalse(
            because: "minLength is meaningless on the array wrapper in Draft-07");
        frag.TryGetProperty("maxLength", out _).Should().BeFalse();
        frag.TryGetProperty("pattern", out _).Should().BeFalse();
        // Item-level constraints route to items.
        var items = frag.GetProperty("items");
        items.GetProperty("type").GetString().Should().Be("string");
        items.GetProperty("minLength").GetInt64().Should().Be(2);
        items.GetProperty("maxLength").GetInt64().Should().Be(32);
        items.GetProperty("pattern").GetString().Should().Be("^[a-z]+$");
        items.TryGetProperty("description", out _).Should().BeFalse(
            because: "description stays on the array wrapper");
    }

    [Fact]
    public void Js003_PropertyNamedState_RaisesError()
    {
        // FR-315: a property named `state` collides with the reserved entity-state slot.
        var src = FixtureWith(@"state: String");
        var (diags, _) = RunAndExtract(src, "state");
        diags.Should().ContainSingle(d => d.RuleId == JsonRuleIds.Js003 && d.Severity == DiagnosticSeverity.Error)
            .Which.Message.Should().Contain("state").And.Contain("reserved");
    }
}
