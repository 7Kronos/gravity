using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
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
    /// <summary>
    /// A .nupkg whose .psmdcp entry decompresses to more than the per-entry cap
    /// throws InvalidDataException rather than silently allocating unbounded
    /// memory. Defence-in-depth against malformed input (FR-3023 risk register).
    /// </summary>
    [Fact]
    public void OversizedEntry_Throws()
    {
        var tmp = Path.Combine(GetTempRoot(), "norm-boundary-bomb");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        Directory.CreateDirectory(tmp);
        try
        {
            // Build a synthetic .nupkg whose .psmdcp entry exceeds the 16 MiB cap.
            // We use highly-compressible content (a long run of the same byte) so the
            // on-disk fixture stays tiny while the decompressed payload trips the cap.
            const long oversized = (16L * 1024 * 1024) + 1024;
            var bombPath = Path.Combine(tmp, "bomb.nupkg");
            using (var fs = new FileStream(bombPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var psmdcp = zip.CreateEntry(
                    "package/services/metadata/core-properties/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.psmdcp",
                    CompressionLevel.Optimal);
                using (var es = psmdcp.Open())
                {
                    var chunk = new byte[64 * 1024];
                    long written = 0;
                    while (written < oversized)
                    {
                        var n = (int)Math.Min(chunk.Length, oversized - written);
                        es.Write(chunk, 0, n);
                        written += n;
                    }
                }
                var rels = zip.CreateEntry("_rels/.rels", CompressionLevel.Optimal);
                using (var es = rels.Open())
                using (var sw = new StreamWriter(es, Encoding.UTF8))
                    sw.Write("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" "
                        + "Target=\"/package/services/metadata/core-properties/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.psmdcp\" "
                        + "Id=\"Rorig\" />"
                        + "</Relationships>");
            }

            var act = () => NupkgNormalizer.Normalize(bombPath, Path.Combine(tmp, "norm.nupkg"));
            act.Should().Throw<InvalidDataException>(
                because: "the normaliser must enforce a per-entry decompression cap as defence-in-depth")
                .WithMessage("*per-entry decompression cap*");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

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

    private static readonly XmlReaderSettings SecureReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private static XDocument ParseSecure(string xml)
    {
        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, SecureReaderSettings);
        // LoadOptions.PreserveWhitespace mirrors RelsRewriter so byte-comparison
        // assertions against the rewriter's output stay consistent.
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static XElement? ReadManifestRelationship(string nupkgPath)
    {
        var relsXml = ReadRelsEntry(nupkgPath);
        var doc = ParseSecure(relsXml);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "Relationship")
            .FirstOrDefault(e => string.Equals(
                (string?)e.Attribute("Type"), ManifestType, StringComparison.Ordinal));
    }

    private static string? ReadCorePropertiesTarget(string nupkgPath)
    {
        var relsXml = ReadRelsEntry(nupkgPath);
        var doc = ParseSecure(relsXml);
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
