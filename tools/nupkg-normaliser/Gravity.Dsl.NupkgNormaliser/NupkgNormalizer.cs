using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Gravity.Dsl.NupkgNormaliser;

/// <summary>
/// Core normaliser logic for <c>.nupkg</c> files (FR-3020, FR-3021, FR-3023).
///
/// <para>
/// The normalisation pipeline:
/// <list type="number">
///   <item><description>Locate the <c>.psmdcp</c> entry and compute its content SHA-256.</description></item>
///   <item><description>Rename the entry to <c>package/services/metadata/core-properties/&lt;sha256&gt;.psmdcp</c>.</description></item>
///   <item><description>Rewrite the single core-properties <c>&lt;Relationship&gt;</c> in <c>_rels/.rels</c> to use the new path.</description></item>
///   <item><description>Re-emit all entries sorted by path under <see cref="StringComparer.Ordinal"/>, with zeroed timestamps and no extra-fields.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Idempotence (FR-3023 / AC-9c.11): a second pass through <see cref="Normalize"/>
/// finds the <c>.psmdcp</c> filename already derived from its content SHA-256, so
/// the rename is a no-op, the <c>_rels/.rels</c> rewrite is a no-op, and the output
/// is byte-identical to the first pass.
/// </para>
/// </summary>
public static class NupkgNormalizer
{
    /// <summary>
    /// Reads the <c>.nupkg</c> at <paramref name="inputPath"/> (opened with
    /// <see cref="FileShare.Read"/> — FR-3021 read-only-on-input) and writes a
    /// normalised copy to <paramref name="outputPath"/> atomically (write to a
    /// sibling <c>.tmp</c> file, then <see cref="File.Move"/> with
    /// <c>overwrite: true</c>).
    /// </summary>
    public static void Normalize(string inputPath, string outputPath)
    {
        var tempOutput = outputPath + ".tmp";

        // FR-3021: open input read-only; never mutate source.
        using (var inputStream = new FileStream(
            inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var inputZip = new ZipArchive(inputStream, ZipArchiveMode.Read, leaveOpen: false))
        {
            // Phase 1 — locate .psmdcp, compute content hash, decide new name.
            var psmdcpEntry = inputZip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith(
                    "package/services/metadata/core-properties/",
                    StringComparison.Ordinal)
                && e.FullName.EndsWith(".psmdcp", StringComparison.Ordinal));

            if (psmdcpEntry is null)
                throw new InvalidOperationException(
                    "Input .nupkg has no .psmdcp entry under package/services/metadata/core-properties/.");

            byte[] psmdcpBytes;
            using (var s = psmdcpEntry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                psmdcpBytes = ms.ToArray();
            }

            var newPsmdcpPath = PsmdcpRenamer.NewPath(psmdcpBytes);

            // Phase 2 — rewrite _rels/.rels (only the .psmdcp-targeting <Relationship>).
            var relsEntry = inputZip.Entries.Single(e => e.FullName == "_rels/.rels");
            string relsXml;
            using (var s = relsEntry.Open())
            using (var r = new StreamReader(s, Encoding.UTF8))
                relsXml = r.ReadToEnd();

            var newRelsXml = RelsRewriter.RewritePsmdcpTarget(
                relsXml, newTarget: "/" + newPsmdcpPath);

            // Phase 3 — build normalised entry set sorted by path.
            var normalised = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var e in inputZip.Entries)
            {
                if (ReferenceEquals(e, psmdcpEntry))
                {
                    normalised[newPsmdcpPath] = psmdcpBytes;
                    continue;
                }
                if (ReferenceEquals(e, relsEntry))
                {
                    normalised["_rels/.rels"] = Encoding.UTF8.GetBytes(newRelsXml);
                    continue;
                }
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                normalised[e.FullName] = ms.ToArray();
            }

            // Phase 4 — emit: sorted order + zero timestamps + no extra-fields (FR-3020 a/b/c).
            using var outFile = new FileStream(
                tempOutput, FileMode.Create, FileAccess.Write, FileShare.None);
            using (var outZip = new ZipArchive(outFile, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var (path, bytes) in normalised)
                {
                    var entry = outZip.CreateEntry(path, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); // FR-3020 (b): minimum valid zip timestamp
                    using var es = entry.Open();
                    es.Write(bytes, 0, bytes.Length);
                }
            }
        }

        // FR-3021: atomic move; overwrites if output already exists (handles input == output case).
        File.Move(tempOutput, outputPath, overwrite: true);
    }
}
