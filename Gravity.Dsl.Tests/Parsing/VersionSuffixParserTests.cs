using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Xunit;

namespace Gravity.Dsl.Tests.Parsing;

/// <summary>
/// Phase 8 / T109 / AC-8.12. Covers positive and negative cases for the
/// <c>@N</c> version suffix on named type references.
/// </summary>
public sealed class VersionSuffixParserTests
{
    // -------- Positive cases (well-formed Money@2, Money@2?, Money@2[], Money@2?[]) --------

    [Theory]
    [InlineData("Money@2", 2, false, false)]
    [InlineData("Money@2?", 2, true, false)]
    [InlineData("Money@2[]", 2, false, true)]
    [InlineData("Money@2?[]", 2, true, true)]
    public void Property_WithVersionSuffix_ParsesToNamedTypeRefVersion(
        string typeSurface, int expectedVersion, bool expectedOptional, bool expectedArray)
    {
        var src = "entity X version 1 { identity id: UUID; properties { payment: " + typeSurface + "; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().BeEmpty();
        result.File.Should().NotBeNull();

        var entity = (EntityDecl)result.File!.Declarations[0];
        var prop = entity.Properties[0];
        var named = prop.Type.Should().BeOfType<NamedTypeRef>().Subject;
        named.Name.Should().Be("Money");
        named.Version.Should().Be(expectedVersion);
        named.IsOptional.Should().Be(expectedOptional);
        named.IsArray.Should().Be(expectedArray);
    }

    [Fact]
    public void Property_WithoutVersionSuffix_HasNullVersion()
    {
        // Sanity: existing Phase 0–3 surface continues to parse to Version: null.
        var src = "entity X version 1 { identity id: UUID; properties { payment: Money; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().BeEmpty();
        var entity = (EntityDecl)result.File!.Declarations[0];
        var named = entity.Properties[0].Type.Should().BeOfType<NamedTypeRef>().Subject;
        named.Version.Should().BeNull();
    }

    // -------- Negative malformed @N cases --------

    [Fact]
    public void MalformedSuffix_MissingLiteral_EmitsParse020()
    {
        var src = "entity X version 1 { identity id: UUID; properties { payment: Money@; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("expected positive integer after '@'"));
        // Recovery: file is usable, Version is null.
        result.File.Should().NotBeNull();
        var named = (NamedTypeRef)((EntityDecl)result.File!.Declarations[0]).Properties[0].Type;
        named.Version.Should().BeNull();
    }

    [Fact]
    public void MalformedSuffix_LeadingZero_EmitsParse020()
    {
        var src = "entity X version 1 { identity id: UUID; properties { payment: Money@01; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("leading zero"));
    }

    [Fact]
    public void MalformedSuffix_Negative_EmitsParse020()
    {
        // '-1' is not a single IntegerLiteral token; the lexer emits LEX001 on '-' and
        // the IntegerLiteral '1' ends up at a column not adjacent to '@', so the
        // parser surfaces a PARSE020 (the precise sub-message is left flexible because
        // both "expected positive integer" and "whitespace" hint at the same root cause).
        var src = "entity X version 1 { identity id: UUID; properties { payment: Money@-1; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020");
    }

    [Fact]
    public void MalformedSuffix_WhitespaceGap_EmitsParse020()
    {
        var src = "entity X version 1 { identity id: UUID; properties { payment: Money@ 2; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("whitespace is not allowed"));
    }

    [Fact]
    public void MalformedSuffix_LeadingPlus_EmitsParse020()
    {
        // '+2' lexes as an unknown char '+' then IntegerLiteral '2'; the parser sees
        // '@' immediately followed by something that is not an adjacent IntegerLiteral.
        var src = "entity X version 1 { identity id: UUID; properties { payment: Money@+2; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020");
    }

    // -------- Negative illegal-position cases (primitive / relation / returns) --------

    [Fact]
    public void Primitive_WithVersionSuffix_EmitsParse020()
    {
        var src = "entity X version 1 { identity id: UUID; properties { count: Int@2; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("not permitted on primitive types"));
    }

    [Fact]
    public void RelationTarget_WithVersionSuffix_EmitsParse020()
    {
        var src = "entity X version 1 { identity id: UUID; "
               + "relations { paid_by: Employee@2 cardinality one; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("not permitted on relation targets"));
    }

    [Fact]
    public void CommandReturns_WithVersionSuffix_EmitsParse020()
    {
        var src = "entity X version 1 { identity id: UUID; events { Submitted {}; } "
               + "commands { Submit() returns SubmissionResult@2 with side_effect Submitted; } }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("not permitted on command return types"));
    }

    // -------- Negative numeric-overflow cases on the bare 'version N' literal --------

    [Fact]
    public void EntityVersion_OverflowsInt32_EmitsParse020_AndDoesNotCrash()
    {
        // Hardening: a 'version N' literal beyond int.MaxValue used to throw
        // OverflowException out of int.Parse. The parser now reports PARSE020
        // and recovers (the file still parses) so a single bad number does not
        // crash the compiler from user input.
        var src = "entity Foo version 99999999999 { identity id: UUID; }\n";
        var result = Parser.Parse("test.gravity", src);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE020"
            && d.Message.Contains("version number must be a positive integer"));
        result.File.Should().NotBeNull(because: "recovery: parsing should not crash on an oversize version literal");
    }
}
