using System.Security;

namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Factory for <c>nuget.config</c> XML content used by consumer csproj fixtures.
/// Produces a config that clears global feeds and adds the specified local feed
/// plus nuget.org, so restore is reproducible regardless of machine-level NuGet
/// configuration. The <paramref name="localFeed"/> value is XML-escaped via
/// <see cref="SecurityElement.Escape"/> as defence in depth — the inputs come
/// from harness-internal <c>ScratchDir.For(...)</c> paths today, but the escape
/// guarantees the emitted XML is well-formed for any path content.
/// </summary>
public static class NuGetConfigFactory
{
    /// <summary>
    /// Returns the XML content of a <c>nuget.config</c> file that clears all
    /// global package sources, adds a local feed at <paramref name="localFeed"/>,
    /// and adds <c>nuget.org</c> as a fallback.
    /// </summary>
    public static string NuGetConfigFor(string localFeed)
    {
        var escapedFeed = SecurityElement.Escape(localFeed) ?? string.Empty;
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<configuration>\n"
            + "  <packageSources>\n"
            + "    <clear />\n"
            + "    <add key=\"gravity-integration-local\" value=\"" + escapedFeed + "\" />\n"
            + "    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />\n"
            + "  </packageSources>\n"
            + "</configuration>\n";
    }
}
