using System.Security;

namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Factory for consumer <c>.csproj</c> fixture files used by integration tests.
/// This is the single source of truth for the consumer csproj template; both the
/// xUnit fast lane and the integration harness slow lane must call this method
/// rather than composing their own templates (FR-3002 / FR-3003 / AC-9c.3).
/// The template is byte-stable: LF newlines only (no <c>Environment.NewLine</c>);
/// backslash escaping on <c>RestorePackagesPath</c> uses ordinal <c>string.Replace</c>.
/// </summary>
public static class ConsumerCsproj
{
    /// <summary>
    /// Writes a <c>Consumer.csproj</c> and a <c>nuget.config</c> under
    /// <paramref name="consumerDir"/> and returns the path to the csproj file.
    /// </summary>
    /// <param name="consumerDir">Directory in which to write the files.</param>
    /// <param name="itemFragment">
    /// Raw XML fragment inserted after the default <c>&lt;PackageReference&gt;</c>
    /// item group, e.g. a <c>&lt;GravityDsl&gt;</c> item group with metadata overrides.
    /// Pass <see cref="string.Empty"/> for the default glob.
    /// </param>
    /// <param name="nugetCacheDir">
    /// Absolute path used as <c>RestorePackagesPath</c> in the csproj, keeping
    /// NuGet cache isolated to the fixture directory.
    /// </param>
    /// <param name="packageVersion">
    /// Version string for the <c>Gravity.Dsl.MsBuild</c> package reference.
    /// </param>
    /// <param name="localFeed">
    /// Absolute path to the local NuGet feed directory written into <c>nuget.config</c>.
    /// </param>
    /// <param name="targetFramework">Target framework moniker; defaults to <c>net9.0</c>.</param>
    /// <returns>Absolute path to the written <c>Consumer.csproj</c> file.</returns>
    public static string Write(
        string consumerDir,
        string itemFragment,
        string nugetCacheDir,
        string packageVersion,
        string localFeed,
        string targetFramework = "net9.0")
    {
        // XML-escape values embedded as element/attribute text. Inputs are harness-
        // internal today (TargetFramework constant, ScratchDir-rooted paths, version
        // extracted from a packed filename), so the escape is defence in depth, not
        // a known-exploit closure. The RestorePackagesPath also takes a
        // backslash-doubling pass for Windows path safety inside the XML element.
        var escapedTfm = SecurityElement.Escape(targetFramework) ?? string.Empty;
        var escapedCache = SecurityElement.Escape(nugetCacheDir.Replace("\\", "\\\\")) ?? string.Empty;
        var escapedVersion = SecurityElement.Escape(packageVersion) ?? string.Empty;

        var csprojPath = Path.Combine(consumerDir, "Consumer.csproj");
        File.WriteAllText(csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            + "  <PropertyGroup>\n"
            + "    <TargetFramework>" + escapedTfm + "</TargetFramework>\n"
            + "    <OutputType>Library</OutputType>\n"
            + "    <RestorePackagesPath>" + escapedCache + "</RestorePackagesPath>\n"
            + "    <Nullable>enable</Nullable>\n"
            + "  </PropertyGroup>\n"
            + "  <ItemGroup>\n"
            + "    <PackageReference Include=\"Gravity.Dsl.MsBuild\" Version=\"" + escapedVersion + "\" />\n"
            + "  </ItemGroup>\n"
            + itemFragment
            + "</Project>\n");
        File.WriteAllText(Path.Combine(consumerDir, "nuget.config"),
            NuGetConfigFactory.NuGetConfigFor(localFeed));
        return csprojPath;
    }
}
