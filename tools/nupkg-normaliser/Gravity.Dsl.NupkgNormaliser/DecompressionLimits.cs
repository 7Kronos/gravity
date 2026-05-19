namespace Gravity.Dsl.NupkgNormaliser;

/// <summary>
/// Defence-in-depth caps on per-entry and total decompressed bytes during
/// <c>.nupkg</c> normalisation. The harness runs only in CI against packages we
/// produced ourselves, so the practical risk of a zip-bomb is near zero — but
/// the cost of the guard is one extra <see cref="System.IO.Stream.CopyTo"/>
/// bound, and a future call from less-trusted code paths is then already safe.
/// Limits are tuned well above the ~30 KiB packs observed in the spike.
/// </summary>
internal static class DecompressionLimits
{
    /// <summary>Maximum decompressed bytes for any single <c>.nupkg</c> entry.</summary>
    internal const long MaxPerEntryBytes = 16L * 1024 * 1024; // 16 MiB

    /// <summary>Maximum total decompressed bytes across all entries of a <c>.nupkg</c>.</summary>
    internal const long MaxTotalBytes = 128L * 1024 * 1024; // 128 MiB
}
