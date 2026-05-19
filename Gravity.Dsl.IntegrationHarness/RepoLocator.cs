using System;
using System.IO;

namespace Gravity.Dsl.IntegrationHarness;

/// <summary>
/// Locates the repository root by walking up from <c>AppContext.BaseDirectory</c>
/// until a directory containing <c>Gravity.Dsl.sln</c> is found (analogous to
/// <c>SamplesLoader.FindRepoSubdirectory</c> in <c>Gravity.Dsl.Tests</c>).
/// </summary>
public static class RepoLocator
{
    /// <summary>
    /// Returns the absolute path of the repository root.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown if <c>Gravity.Dsl.sln</c> cannot be found by walking up the
    /// directory tree from <c>AppContext.BaseDirectory</c>.
    /// </exception>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gravity.Dsl.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Gravity.Dsl.sln walking up from: " + AppContext.BaseDirectory);
    }
}
