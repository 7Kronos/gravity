using System;
using System.IO;
using Gravity.Dsl.IntegrationHarness.Shared;

namespace Gravity.Dsl.IntegrationHarness;

/// <summary>
/// Startup drift-warning for SDK version mismatches (FR-3001).
/// Never throws, never exits non-zero — drift is informational only.
/// </summary>
public static class SdkVersionCheck
{
    /// <summary>
    /// Runs <c>dotnet --version</c> and emits a warning to stderr if the
    /// observed version differs from <paramref name="pinned"/> (major, minor,
    /// or patch-level divergence all trigger the warning).
    /// </summary>
    public static void WarnIfDrift(string pinned)
    {
        try
        {
            var repoRoot = RepoLocator.FindRepoRoot();
            var (exit, stdout, _) = ProcessRunner.RunDotnetCapture("--version", repoRoot);
            if (exit != 0) return;
            var observed = stdout.Trim();
            if (!string.Equals(observed, pinned, StringComparison.Ordinal))
            {
                Console.Error.Write(
                    "[harness] WARNING: observed dotnet --version " + observed
                    + ", pinned " + pinned
                    + "; cross-runner divergence possible\n");
            }
        }
        catch
        {
            // Best effort — never fail startup due to version check.
        }
    }
}
