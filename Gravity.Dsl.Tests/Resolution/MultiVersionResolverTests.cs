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

/// <summary>
/// Phase 8 (P8b) resolver tests for multi-version coexistence (FR-120..FR-127).
/// Pins AC-8.7 (chained coexistence), AC-8.8 first half (RES004 on broken chain),
/// AC-8.13 (unqualified resolves to max, qualified exact, missing-version RES003),
/// and AC-8.13b (imports-transitive scope filtering).
/// </summary>
public sealed class MultiVersionResolverTests
{
    private static SourceFile Parse(string path)
    {
        var src = File.ReadAllText(path);
        var r = Parser.Parse(path, src);
        r.Diagnostics.Should().BeEmpty(because: "input {0} should parse", path);
        r.File.Should().NotBeNull();
        return r.File!;
    }

    private static List<SourceFile> ParseAll(params string[] paths)
    {
        var files = new List<SourceFile>();
        foreach (var p in paths) files.Add(Parse(p));
        return files;
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "versioning", "resolver");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/versioning/resolver not found");
    }

    // ---- T127: chained coexistence (AC-8.7) ----

    [Fact]
    public void ChainOkTwoVersions_NoDiagnostics_BothDeclsPresent()
    {
        var root = Root();
        var path = Path.Combine(root, "chain_ok_two_versions.gravity");
        var files = ParseAll(path);
        var result = Resolver.Resolve(files, root);

        result.Diagnostics.Should().BeEmpty();
        result.Model.Should().NotBeNull();
        result.Model!.Declarations.Keys.Should().Contain(new DeclKey("ops.Employee", 1));
        result.Model.Declarations.Keys.Should().Contain(new DeclKey("ops.Employee", 2));
    }

    [Fact]
    public void ChainOkThreeVersions_NoDiagnostics_VersionIndexIs123()
    {
        var root = Root();
        var path = Path.Combine(root, "chain_ok_three_versions.gravity");
        var files = ParseAll(path);
        var result = Resolver.Resolve(files, root);

        result.Diagnostics.Should().BeEmpty();
        result.Model.Should().NotBeNull();
        var versions = result.Model!.VersionIndex["ops.Employee"].ToArray();
        versions.Should().Equal(new[] { 1, 2, 3 });
    }

    // ---- T128: broken chain (AC-8.8 first half) ----

    [Fact]
    public void ChainMissing_TwoUnchainedVersions_EmitsExactlyOneRes004()
    {
        var root = Root();
        var path = Path.Combine(root, "chain_missing.gravity");
        var files = ParseAll(path);
        var result = Resolver.Resolve(files, root);

        var res004 = result.Diagnostics.Where(d => d.RuleId == "RES004").ToList();
        res004.Should().ContainSingle(because: "exactly one RES004 at the (1,2) gap");
        res004[0].Message.Should().Contain("multi-version coexistence requires a deprecates chain");
        res004[0].Message.Should().Contain("ops.Employee");
    }

    [Fact]
    public void ChainSkippedLink_EmitsOneRes004AtFirstGap()
    {
        var root = Root();
        var path = Path.Combine(root, "chain_skipped_link.gravity");
        var files = ParseAll(path);
        var result = Resolver.Resolve(files, root);

        // (1,2) is unchained -> RES004; (2,3) is chained -> nothing.
        var res004 = result.Diagnostics.Where(d => d.RuleId == "RES004").ToList();
        res004.Should().ContainSingle();
        res004[0].Message.Should().Contain("multi-version coexistence requires a deprecates chain");
        // Version-index assertion (full [1,2,3]) is exercised on chain_ok_three_versions,
        // which has zero fatal diagnostics; RES004 here is fatal so model is null.
    }

    // ---- T129: unqualified -> max, qualified -> exact, missing-version RES003 (AC-8.13) ----

    [Fact]
    public void Unqualified_ResolvesToMaxVersion()
    {
        var root = Root();
        var path = Path.Combine(root, "unqualified_resolves_to_max.gravity");
        var files = ParseAll(path);
        var (result, bindings) = ResolveWithBindings(files, root);

        result.Diagnostics.Should().BeEmpty();
        result.Model.Should().NotBeNull();

        // Find the unqualified Project ref on Holder.lead_project; it must bind to (ops.Project, 2).
        var named = FindPropertyTypeRef(files[0], holderEntity: "Holder", propertyName: "lead_project");
        bindings.TryGetValue(named, out var key).Should().BeTrue();
        key.Should().Be(new DeclKey("ops.Project", 2));
    }

    [Fact]
    public void Qualified_ResolvesToExactVersion()
    {
        var root = Root();
        var path = Path.Combine(root, "qualified_resolves_exact.gravity");
        var files = ParseAll(path);
        var (result, bindings) = ResolveWithBindings(files, root);

        result.Diagnostics.Should().BeEmpty();
        result.Model.Should().NotBeNull();

        var named = FindPropertyTypeRef(files[0], holderEntity: "Holder", propertyName: "lead_project_legacy");
        named.Version.Should().Be(1);
        bindings.TryGetValue(named, out var key).Should().BeTrue();
        key.Should().Be(new DeclKey("ops.Project", 1));
    }

    [Fact]
    public void Qualified_MissingVersion_EmitsRes003_WithVersionListInMessage()
    {
        var root = Root();
        var path = Path.Combine(root, "qualified_missing_version.gravity");
        var files = ParseAll(path);
        var result = Resolver.Resolve(files, root);

        var res003 = result.Diagnostics.Where(d => d.RuleId == "RES003").ToList();
        res003.Should().ContainSingle();
        res003[0].Message.Should().Contain("'Project@5'");
        res003[0].Message.Should().Contain("1, 2");
        res003[0].Message.Should().Contain("is not declared");
    }

    // ---- AC-8.13b: cross-file imports-transitive scope ----

    [Fact]
    public void CrossFileImports_UnqualifiedBindsToImportedVersionOnly()
    {
        var crossRoot = Path.Combine(Root(), "cross_file_imports");
        var aPath = Path.Combine(crossRoot, "a.gravity");
        var v1Path = Path.Combine(crossRoot, "defs_v1.gravity");
        var v2Path = Path.Combine(crossRoot, "defs_v2.gravity");
        var files = ParseAll(aPath, v1Path, v2Path);
        var (result, bindings) = ResolveWithBindings(files, crossRoot);

        // Model-wide: Project@1 and Project@2 coexist with a deprecates chain -> zero RES004.
        result.Diagnostics.Where(d => d.RuleId == "RES004").Should().BeEmpty();
        result.Model.Should().NotBeNull();
        result.Model!.VersionIndex["ops.Project"].ToArray().Should().Equal(new[] { 1, 2 });

        // File a.gravity imports only defs_v1; the unqualified Project ref in a.gravity binds to v1.
        var aFile = files.First(f => f.Path == aPath);
        var named = FindPropertyTypeRef(aFile, holderEntity: "Holder", propertyName: "lead_project");
        bindings.TryGetValue(named, out var key).Should().BeTrue();
        key.Should().Be(new DeclKey("ops.Project", 1));
    }

    // ---- helpers ----

    private static (ResolveResult Result, IReadOnlyDictionary<NamedTypeRef, DeclKey> Bindings)
        ResolveWithBindings(IReadOnlyList<SourceFile> files, string inputRoot)
        => Resolver.ResolveWithBindings(files, inputRoot);

    private static NamedTypeRef FindPropertyTypeRef(SourceFile file, string holderEntity, string propertyName)
    {
        foreach (var decl in file.Declarations)
        {
            if (decl is EntityDecl ent && ent.Name == holderEntity)
            {
                foreach (var prop in ent.Properties)
                {
                    if (prop.Name == propertyName && prop.Type is NamedTypeRef n)
                    {
                        return n;
                    }
                }
            }
        }
        throw new System.InvalidOperationException(
            $"property '{propertyName}' on entity '{holderEntity}' not found in {file.Path}");
    }
}
