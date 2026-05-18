using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Parsing;

public sealed class RoundTripTests
{
    public static IEnumerable<object[]> AllSources()
    {
        var sampleRoot = ParserTests.SampleRoot();
        foreach (var path in Directory.EnumerateFiles(sampleRoot, "*.gravity"))
        {
            yield return new object[] { path };
        }
        var fixtureRoot = FixtureRoot();
        foreach (var path in Directory.EnumerateFiles(fixtureRoot, "*.gravity"))
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(AllSources))]
    public void Parse_Write_Parse_YieldsStructurallyEqualAst(string path)
    {
        var src = File.ReadAllText(path);
        var firstParse = Parser.Parse(path, src);
        firstParse.Diagnostics.Should().BeEmpty(because: "input {0} should parse", path);
        firstParse.File.Should().NotBeNull();

        var written = SourceWriter.Write(firstParse.File!);
        var secondParse = Parser.Parse(path, written);
        secondParse.Diagnostics.Should().BeEmpty(because: "written canonical form should re-parse: {0}", written);
        secondParse.File.Should().NotBeNull();

        SpanIgnoringEquality.Equal(firstParse.File, secondParse.File).Should().BeTrue(
            because: "AST should be structurally equal after Parse -> Write -> Parse. Canonical:\n{0}", written);
    }

    [Fact]
    public void TotalFixtureCount_AtLeast_FiveFixturesPlusThreeSamples()
    {
        AllSources().Count().Should().BeGreaterOrEqualTo(8);
    }

    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "parser");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/parser not found");
    }
}
