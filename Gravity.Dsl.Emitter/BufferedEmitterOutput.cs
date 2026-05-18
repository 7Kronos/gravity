using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// In-memory <see cref="IEmitterOutput"/> implementation. Calls to
/// <see cref="WriteFile"/> populate a dictionary keyed by relative path. The host
/// commits the buffer to disk by sorting relative paths under
/// <see cref="StringComparer.Ordinal"/> and writing one file at a time with UTF-8
/// (no BOM) and LF line endings. This guarantees on-disk file creation order is
/// deterministic regardless of emitter authoring style or thread scheduling
/// (plan.md §4, AC-6a).
/// </summary>
public sealed class BufferedEmitterOutput : IEmitterOutput
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <inheritdoc/>
    public void WriteFile(string relativePath, string contents)
    {
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
        if (contents is null) throw new ArgumentNullException(nameof(contents));
        var normalized = NormalizeRelativePath(relativePath);
        // Defence-in-depth against malicious or buggy emitters (Phase 9 plugin scenario).
        // The host already constrains the emitter output root, but a write whose
        // relative path is rooted or contains a parent-directory segment would escape
        // that root once Path.Combine resolves it. Reject both forms here as the cheap
        // fail-fast layer; CommitTo applies a second canonicalisation-based check.
        if (Path.IsPathRooted(normalized))
        {
            throw new ArgumentException("relative path required: " + relativePath, nameof(relativePath));
        }
        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                throw new ArgumentException("relative path required: " + relativePath, nameof(relativePath));
            }
        }
        lock (_gate)
        {
            _files[normalized] = contents;
        }
    }

    /// <summary>
    /// Snapshot the buffered files in deterministic order. Returned as an
    /// <see cref="ImmutableSortedDictionary{TKey,TValue}"/> so callers iterate in
    /// ordinal order without re-sorting.
    /// </summary>
    public ImmutableSortedDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return _files.ToImmutableSortedDictionary(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Write every buffered file under <paramref name="outputRoot"/>. Files are
    /// written in ordinal order by relative path; parent directories are created
    /// as needed. Existing files are overwritten; files outside the buffer are
    /// left untouched (plan.md §3.6: "create-or-overwrite per file").
    /// </summary>
    public void CommitTo(string outputRoot)
    {
        if (outputRoot is null) throw new ArgumentNullException(nameof(outputRoot));
        Directory.CreateDirectory(outputRoot);
        string canonicalRoot = Path.GetFullPath(outputRoot);
        foreach (var kv in Snapshot())
        {
            var fullPath = Path.Combine(canonicalRoot, kv.Key);
            string canonicalFullPath = Path.GetFullPath(fullPath);
            // Second layer of defence (the WriteFile sanitizer is the first):
            // refuse to write any buffered file whose canonical path escapes the
            // configured output root.
            if (!IsWithinRoot(canonicalFullPath, canonicalRoot))
            {
                throw new InvalidOperationException(
                    "buffered file '" + kv.Key + "' resolves outside output root '" + canonicalRoot + "'");
            }
            var dir = Path.GetDirectoryName(canonicalFullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            WriteFileBytes(canonicalFullPath, kv.Value);
        }
    }

    private static bool IsWithinRoot(string canonicalCandidate, string canonicalRoot)
    {
        if (string.Equals(canonicalCandidate, canonicalRoot, StringComparison.Ordinal)) return true;
        string prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalCandidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static void WriteFileBytes(string path, string contents)
    {
        // UTF-8 without BOM; LF line endings only (plan.md §4).
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, encoding) { NewLine = "\n" };
        writer.Write(contents);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        // Force forward slashes inside the relative key so dictionary lookups and
        // ordinal sorts behave identically on Windows and Unix.
        return relativePath.Replace('\\', '/');
    }
}
