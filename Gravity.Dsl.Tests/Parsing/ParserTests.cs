using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Xunit;

namespace Gravity.Dsl.Tests.Parsing;

public sealed class ParserTests
{
    private static SourceFile ParseOk(string source, string path = "test.gravity")
    {
        var r = Parser.Parse(path, source);
        r.Diagnostics.Should().BeEmpty();
        r.File.Should().NotBeNull();
        return r.File!;
    }

    [Fact]
    public void NamespaceAndImport_ParseToTopOfFile()
    {
        var src = "namespace hr;\nimport \"Employee.gravity\";\n";
        var file = ParseOk(src);
        file.Namespace!.Name.Should().Be("hr");
        file.Imports.Should().HaveCount(1);
        file.Imports[0].RelativePath.Should().Be("Employee.gravity");
        file.Declarations.Should().BeEmpty();
    }

    [Fact]
    public void DottedNamespace_Concatenates()
    {
        var src = "namespace hr.payroll;\n";
        var file = ParseOk(src);
        file.Namespace!.Name.Should().Be("hr.payroll");
    }

    [Fact]
    public void ValueType_WithFields_Parses()
    {
        var src = "type Result { ok: Boolean; message: String?; }\n";
        var file = ParseOk(src);
        var vt = (ValueTypeDecl)file.Declarations[0];
        vt.Name.Should().Be("Result");
        vt.Fields.Should().HaveCount(2);
        vt.Fields[0].Name.Should().Be("ok");
        vt.Fields[1].Type.Should().BeOfType<PrimitiveTypeRef>()
            .Which.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Enum_WithTrailingComma_Parses()
    {
        var src = "enum Color { Red, Green, Blue, }\n";
        var file = ParseOk(src);
        var en = (EnumDecl)file.Declarations[0];
        en.Variants.Should().Equal("Red", "Green", "Blue");
    }

    [Fact]
    public void EntitySkeleton_OnlyIdentity_Parses()
    {
        var src = "entity X version 1 { identity id: UUID; }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Name.Should().Be("X");
        e.Version.Should().Be(1);
        e.Identity.FieldName.Should().Be("id");
        e.Relations.Should().BeEmpty();
        e.Properties.Should().BeEmpty();
        e.Events.Should().BeEmpty();
        e.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Entity_WithDeprecates_RecordsClause()
    {
        var src = "entity X version 2 deprecates version 1 until \"2026-12-31\" { identity id: UUID; }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Deprecates.Should().NotBeNull();
        e.Deprecates!.Version.Should().Be(1);
        e.Deprecates.UntilIso8601.Should().Be("2026-12-31");
    }

    [Fact]
    public void Relations_OptionalAndSemantic_Parsed()
    {
        var src = "entity X version 1 { identity id: UUID; relations { a: Y cardinality one; b: Y? cardinality one semantic owns; } }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Relations.Should().HaveCount(2);
        e.Relations[1].IsOptional.Should().BeTrue();
        e.Relations[1].Semantic.Should().Be("owns");
    }

    [Fact]
    public void Property_WithAnnotation_Parsed()
    {
        var src = "entity X version 1 { identity id: UUID; properties { name: String @csharp(attr: \"Display\"); } }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Properties[0].Annotations.Should().HaveCount(1);
        e.Properties[0].Annotations[0].Namespace.Should().Be("csharp");
        e.Properties[0].Annotations[0].Arguments.Should().ContainKey("attr");
    }

    [Fact]
    public void Lifecycle_StatesAndTransitions_Parsed()
    {
        var src = "entity X version 1 { identity id: UUID; lifecycle { states { A, B; } transitions { A -> B on Done; } } events { Done {}; } }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Lifecycle.States.Should().Equal("A", "B");
        e.Lifecycle.Transitions.Should().HaveCount(1);
        e.Lifecycle.Transitions[0].From.Should().Be("A");
        e.Lifecycle.Transitions[0].To.Should().Be("B");
        e.Lifecycle.Transitions[0].OnEvent.Should().Be("Done");
    }

    [Fact]
    public void Events_EmptyPayload_Legal()
    {
        var src = "entity X version 1 { identity id: UUID; events { Resubmitted {}; Done { at: DateTime; }; } }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Events.Should().HaveCount(2);
        e.Events[0].Payload.Should().BeEmpty();
        e.Events[1].Payload.Should().HaveCount(1);
    }

    [Fact]
    public void Command_AllSubClauses_Parsed()
    {
        var src = "entity X version 1 { identity id: UUID; events { E {}; } commands { Do(a: Int, b: String?) returns R with side_effect E; } }\ntype R { ok: Boolean; }\n";
        var file = ParseOk(src);
        var e = (EntityDecl)file.Declarations[0];
        e.Commands[0].Name.Should().Be("Do");
        e.Commands[0].Arguments.Should().HaveCount(2);
        e.Commands[0].ReturnsType.Should().Be("R");
        e.Commands[0].SideEffectEvent.Should().Be("E");
    }

    [Theory]
    [InlineData("Employee.gravity")]
    [InlineData("Project.gravity")]
    [InlineData("TimeEntry.gravity")]
    public void Sample_ParsesWithoutDiagnostics(string fileName)
    {
        var path = Path.Combine(SampleRoot(), fileName);
        var source = File.ReadAllText(path);
        var r = Parser.Parse(path, source);
        r.Diagnostics.Should().BeEmpty(because: "sample {0} should parse cleanly", fileName);
        r.File.Should().NotBeNull();
    }

    [Fact]
    public void DeeplyNestedInput_Emits_PARSE010()
    {
        // Synthesize hostile input that, parsed under a small depth cap, drives the
        // recursive-descent guard past the limit. With the production cap of 256 the
        // grammar does not naturally reach this depth, so the test exercises the
        // guard via the test-only depth override.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            sb.Append("@a").Append(i).Append(' ');
        }
        sb.Append("entity X version 1 { identity id: UUID; }\n");
        var src = sb.ToString();

        // Cap at 1: ParseSourceFile already takes _depth=1, so ParseAnnotations's
        // EnterDepth attempt would take depth=2 > cap, firing PARSE010.
        var result = Parser.ParseWithDepthCap("hostile.gravity", src, maxDepth: 1);
        result.Diagnostics.Should().Contain(d => d.RuleId == "PARSE010"
            && d.Message.Contains("maximum nesting depth"));
    }

    [Fact]
    public void NaturallyDeepInput_DoesNotTrip_PARSE010_AtProductionCap()
    {
        // Sanity: every registry sample parses without PARSE010 at the production cap.
        var path = Path.Combine(SampleRoot(), "Employee.gravity");
        var source = File.ReadAllText(path);
        var result = Parser.Parse(path, source);
        result.Diagnostics.Should().NotContain(d => d.RuleId == "PARSE010");
    }

    internal static string SampleRoot()
    {
        // Walk up to find /workspace/gravity/samples/registry from the test binary.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "registry");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("samples/registry not found");
    }
}
