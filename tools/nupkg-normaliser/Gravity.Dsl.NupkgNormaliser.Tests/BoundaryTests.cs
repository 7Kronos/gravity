using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Gravity.Dsl.NupkgNormaliser;
using Xunit;

namespace Gravity.Dsl.NupkgNormaliser.Tests;

/// <summary>
/// Boundary tests that pin the critical LD-22 / FR-3020 step (e) contract:
/// the manifest-pointer <c>&lt;Relationship&gt;</c> (the one with
/// <c>Type="...packaging/2010/07/manifest"</c>) MUST NOT be rewritten, while the
/// core-properties <c>&lt;Relationship&gt;</c> (pointing at the <c>.psmdcp</c>)
/// MUST have its <c>Target</c> updated to the deterministic path.
/// </summary>
public sealed class BoundaryTests
{
    private const string ManifestType =
        "http://schemas.microsoft.com/packaging/2010/07/manifest";
    private const string CorePropertiesType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    private static readonly string FixtureDir = Path.Combine(
        FindRepoRoot(), "tests", "fixtures", "nupkg-normaliser");

    private static string GetTempRoot()
        => Environment.GetEnvironmentVariable("TMPDIR")
           ?? Environment.GetEnvironmentVariable("TEMP")
           ?? "/tmp";

    /// <summary>
    /// The manifest-pointer <c>&lt;Relationship&gt;</c> (Id="RAC971DF315D82D83") must be
    /// byte-identical between pack-a's normalised output and pack-b's normalised output.
    /// Pins LD-22 / FR-3020 (e): "the first Relationship is content-deterministic and
    /// MUST NOT be touched".
    /// </summary>
    [Fact]
    public void ManifestPointerRelationship_NotRewritten()
    {
        var tmp = Path.Combine(GetTempRoot(), "norm-boundary-manifest");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            var normA = Path.Combine(tmp, "norm-a.nupkg");
            var normB = Path.Combine(tmp, "norm-b.nupkg");
            NupkgNormalizer.Normalize(Path.Combine(FixtureDir, "pack-a.nupkg"), normA);
            NupkgNormalizer.Normalize(Path.Combine(FixtureDir, "pack-b.nupkg"), normB);

            var manifestRelA = ReadManifestRelationship(normA);
            var manifestRelB = ReadManifestRelationship(normB);

            manifestRelA.Should().NotBeNull("pack-a normalised output must contain a manifest-pointer Relationship");
            manifestRelB.Should().NotBeNull("pack-b normalised output must contain a manifest-pointer Relationship");

            // The entire serialised XML of the manifest-pointer <Relationship> element
            // must be identical between the two normalised outputs.
            manifestRelA!.ToString().Should().Be(manifestRelB!.ToString(),
                because: "the manifest-pointer Relationship is content-deterministic and must not be rewritten (LD-22 / FR-3020 e)");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    /// <summary>
    /// The core-properties <c>&lt;Relationship&gt;</c> <c>Target</c> must change
    /// from the input's random GUID value to a value ending in <c>.psmdcp</c>.
    /// Pins that the rewriter is not a no-op (defence-in-depth per plan.md §4.1).
    /// </summary>
    [Fact]
    public void PsmdcpPointerRelationship_TargetRewritten()
    {
        var tmp = Path.Combine(GetTempRoot(), "norm-boundary-psmdcp");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            var inputPath = Path.Combine(FixtureDir, "pack-a.nupkg");
            var outputPath = Path.Combine(tmp, "norm-a.nupkg");
            NupkgNormalizer.Normalize(inputPath, outputPath);

            var inputTarget = ReadCorePropertiesTarget(inputPath);
            var outputTarget = ReadCorePropertiesTarget(outputPath);

            outputTarget.Should().NotBeNull("normalised output must contain a core-properties Relationship");
            outputTarget.Should().EndWith(".psmdcp",
                because: "normalised Target must end with .psmdcp (FR-3020 d)");
            outputTarget.Should().NotBe(inputTarget,
                because: "the Target must change from the random GUID path to the content-derived SHA-256 path (rewriter must not be a no-op)");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static XElement? ReadManifestRelationship(string nupkgPath)
    {
        var relsXml = ReadRelsEntry(nupkgPath);
        var doc = XDocument.Parse(relsXml);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "Relationship")
            .FirstOrDefault(e => string.Equals(
                (string?)e.Attribute("Type"), ManifestType, StringComparison.Ordinal));
    }

    private static string? ReadCorePropertiesTarget(string nupkgPath)
    {
        var relsXml = ReadRelsEntry(nupkgPath);
        var doc = XDocument.Parse(relsXml);
        var rel = doc.Descendants()
            .Where(e => e.Name.LocalName == "Relationship")
            .FirstOrDefault(e => string.Equals(
                (string?)e.Attribute("Type"), CorePropertiesType, StringComparison.Ordinal));
        return rel is null ? null : (string?)rel.Attribute("Target");
    }

    private static string ReadRelsEntry(string nupkgPath)
    {
        using var zip = ZipFile.OpenRead(nupkgPath);
        var entry = zip.Entries.Single(e => e.FullName == "_rels/.rels");
        using var s = entry.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gravity.Dsl.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot locate repo root (no Gravity.Dsl.sln found)");
    }
}
