using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;
using Xunit;

namespace Gravity.Dsl.Tests.Resolution;

public sealed class ResolverTests
{
    private static List<SourceFile> ParseAll(params string[] paths)
    {
        var files = new List<SourceFile>();
        foreach (var p in paths)
        {
            var src = File.ReadAllText(p);
            var r = Parser.Parse(p, src);
            r.Diagnostics.Should().BeEmpty(because: "input {0} should parse", p);
            r.File.Should().NotBeNull();
            files.Add(r.File!);
        }
        return files;
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "resolver");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/resolver not found");
    }

    [Fact]
    public void ImportOk_ResolvesCleanly()
    {
        var root = Path.Combine(Root(), "import_ok");
        var files = ParseAll(Path.Combine(root, "Defs.gravity"), Path.Combine(root, "User.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().BeEmpty();
        result.Model.Should().NotBeNull();
        result.Model!.Declarations.Keys.Should().Contain(k => k.Fqn == "ok.Holder");
        result.Model.Declarations.Keys.Should().Contain(k => k.Fqn == "ok.Shared");
    }

    [Fact]
    public void ImportCycle_EmitsRes001()
    {
        var root = Root();
        var files = ParseAll(
            Path.Combine(root, "cycle_a", "A.gravity"),
            Path.Combine(root, "cycle_b", "B.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().Contain(d => d.RuleId == "RES001");
    }

    [Fact]
    public void MissingImport_EmitsRes002()
    {
        var root = Path.Combine(Root(), "missing_import");
        var files = ParseAll(Path.Combine(root, "Use.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().ContainSingle(d => d.RuleId == "RES002");
        result.Diagnostics.First(d => d.RuleId == "RES002").Message.Should().Contain("could not be resolved");
    }

    [Fact]
    public void MissingDefinition_EmitsRes003_WithDistinctMessage()
    {
        var root = Path.Combine(Root(), "missing_def");
        var files = ParseAll(Path.Combine(root, "Use.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().Contain(d => d.RuleId == "RES003");
        var msg = result.Diagnostics.First(d => d.RuleId == "RES003").Message;
        msg.Should().Contain("not defined or imported");
        msg.Should().NotContain("could not be resolved", because: "RES003 must be textually distinct from RES002");
    }

    [Fact]
    public void MissingDefinition_IsFatal_ModelIsNull_RES003()
    {
        var root = Path.Combine(Root(), "missing_def");
        var files = ParseAll(Path.Combine(root, "Use.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().Contain(d => d.RuleId == "RES003" && d.Severity == DiagnosticSeverity.Error);
        result.Model.Should().BeNull(
            because: "RES003 is fatal: generated C# would not compile against an unresolved name");
    }

    [Fact]
    public void DuplicateFqn_EmitsRes004()
    {
        var root = Path.Combine(Root(), "duplicate_fqn");
        var files = ParseAll(Path.Combine(root, "A.gravity"), Path.Combine(root, "B.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().ContainSingle(d => d.RuleId == "RES004");
        result.Diagnostics.First(d => d.RuleId == "RES004").Message.Should().Contain("dup.Twin");
    }

    [Fact]
    public void AmbiguousImport_EmitsRes005()
    {
        var root = Path.Combine(Root(), "ambiguous");
        var files = ParseAll(
            Path.Combine(root, "A.gravity"),
            Path.Combine(root, "B.gravity"),
            Path.Combine(root, "Importer.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().Contain(d => d.RuleId == "RES005");
        result.Diagnostics.First(d => d.RuleId == "RES005").Message.Should().Contain("Conflict");
    }

    [Fact]
    public void Import_AbsolutePath_IsRejected_RES006()
    {
        var root = Path.Combine(Root(), "import_ok");
        // Synthesize a source file with a rooted import without writing it to disk.
        var rootedImport = OperatingSystem.IsWindows() ? "C:\\etc\\passwd" : "/etc/passwd";
        var src = "namespace ok;\nimport \"" + rootedImport.Replace("\\", "\\\\") + "\";\n"
            + "entity X version 1 { identity id: UUID; }\n";
        var path = Path.Combine(root, "Absolute.gravity");
        var parsed = Parser.Parse(path, src);
        parsed.Diagnostics.Should().BeEmpty();
        var result = Resolver.Resolve(new[] { parsed.File! }, root);
        result.Diagnostics.Should().Contain(d => d.RuleId == "RES006" && d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("must be a relative path within the input root"));
        result.Model.Should().BeNull(because: "RES006 is fatal");
    }

    [Fact]
    public void Import_DotDotEscape_IsRejected_RES006()
    {
        var root = Path.Combine(Root(), "import_ok");
        var src = "namespace ok;\nimport \"../../escape.gravity\";\n"
            + "entity X version 1 { identity id: UUID; }\n";
        var path = Path.Combine(root, "Escape.gravity");
        var parsed = Parser.Parse(path, src);
        parsed.Diagnostics.Should().BeEmpty();
        var result = Resolver.Resolve(new[] { parsed.File! }, root);
        result.Diagnostics.Should().Contain(d => d.RuleId == "RES006" && d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("resolves outside the input root"));
        result.Model.Should().BeNull(because: "RES006 is fatal");
    }

    [Fact]
    public void Import_Within_InputRoot_IsAccepted()
    {
        var root = Path.Combine(Root(), "import_ok");
        var files = ParseAll(Path.Combine(root, "Defs.gravity"), Path.Combine(root, "User.gravity"));
        var result = Resolver.Resolve(files, root);
        result.Diagnostics.Should().NotContain(d => d.RuleId == "RES006");
        result.Model.Should().NotBeNull();
    }
}
