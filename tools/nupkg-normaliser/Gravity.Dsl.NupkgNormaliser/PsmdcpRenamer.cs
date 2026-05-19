using System;
using System.Security.Cryptography;

namespace Gravity.Dsl.NupkgNormaliser;

/// <summary>
/// Computes the deterministic new path for a <c>.psmdcp</c> entry using the
/// SHA-256 of its decompressed bytes (FR-3020 step d). The result is the full
/// 64-character lowercase hex digest followed by <c>.psmdcp</c>, placed under
/// the canonical <c>package/services/metadata/core-properties/</c> prefix.
/// Running this on an already-renamed entry (whose filename IS the SHA-256) is
/// a no-op — idempotence falls out of the content-derived naming (FR-3023 / AC-9c.11).
/// </summary>
internal static class PsmdcpRenamer
{
    private const string Prefix = "package/services/metadata/core-properties/";

    /// <summary>
    /// Returns the new zip entry path for the <c>.psmdcp</c> file whose
    /// decompressed content is <paramref name="decompressedBytes"/>.
    /// </summary>
    public static string NewPath(byte[] decompressedBytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(decompressedBytes))
            .ToLowerInvariant();
        return Prefix + hash + ".psmdcp";
    }
}
