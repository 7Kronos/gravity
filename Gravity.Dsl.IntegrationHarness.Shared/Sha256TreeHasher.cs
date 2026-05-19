using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Hashes an entire directory tree into a single deterministic SHA-256 hex digest.
/// Used by AC-9.5 (CLI/MSBuild parity) and forward-compatible for any future
/// "two trees must agree byte-for-byte" assertion. Iteration order is
/// <see cref="StringComparer.Ordinal"/> on the relative path so the digest is
/// stable across file-system enumeration order variations (FR-3002).
/// </summary>
public static class Sha256TreeHasher
{
    /// <summary>
    /// Walks every file under <paramref name="rootDir"/> recursively, folds their
    /// (relativePath, SHA-256-of-contents) tuples — sorted by relative path under
    /// <see cref="StringComparer.Ordinal"/> — into a single SHA-256 digest, and
    /// returns it as a 64-character lowercase hex string.
    /// </summary>
    public static string HashTree(string rootDir)
    {
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(rootDir, file)
                .Replace('\\', '/');
            entries[relativePath] = SHA256.HashData(File.ReadAllBytes(file));
        }

        using var outer = SHA256.Create();
        foreach (var (path, contentHash) in entries)
        {
            var pathBytes = Encoding.UTF8.GetBytes(path + "\n");
            outer.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            var hexBytes = Encoding.UTF8.GetBytes(
                Convert.ToHexString(contentHash).ToLowerInvariant() + "\n");
            outer.TransformBlock(hexBytes, 0, hexBytes.Length, null, 0);
        }
        outer.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(outer.Hash!).ToLowerInvariant();
    }
}
