namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Shared helpers for locating the packed <c>Gravity.Dsl.MsBuild.*.nupkg</c> in
/// a per-subcommand scratch feed and extracting the version suffix from its
/// filename. Used by all five MsBuild-consuming subcommands (HookOrder,
/// ItemMetadataOverride, NoGlobalTool, IncrementalBuild, EmptyInput).
/// </summary>
public static class NupkgLookup
{
    private const string MsBuildPackagePrefix = "Gravity.Dsl.MsBuild.";

    /// <summary>
    /// Returns the absolute path to the (lexicographically first under
    /// <see cref="StringComparer.Ordinal"/>) <c>Gravity.Dsl.MsBuild.*.nupkg</c>
    /// inside <paramref name="feedDir"/>. Ordering is explicit so multiple
    /// packs in the same feed pick a stable file across runs / file systems.
    /// </summary>
    public static string FindMsBuildNupkg(string feedDir)
    {
        var files = Directory.GetFiles(feedDir, MsBuildPackagePrefix + "*.nupkg")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException(
                "No " + MsBuildPackagePrefix + "*.nupkg found in " + feedDir);
        return files[0];
    }

    /// <summary>
    /// Extracts the version suffix from a packed <c>Gravity.Dsl.MsBuild.*.nupkg</c>
    /// filename (e.g. <c>Gravity.Dsl.MsBuild.0.1.0.nupkg</c> -> <c>0.1.0</c>).
    /// Returns <c>0.1.0</c> as a fallback if the prefix does not match.
    /// </summary>
    public static string ExtractVersion(string nupkgPath)
    {
        var filename = Path.GetFileNameWithoutExtension(nupkgPath);
        return filename.StartsWith(MsBuildPackagePrefix, StringComparison.Ordinal)
            ? filename.Substring(MsBuildPackagePrefix.Length)
            : "0.1.0";
    }
}
