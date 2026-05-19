using System;
using System.Globalization;
using System.IO;

namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// TMPDIR-rooted, counter-named scratch directory factory (FR-3045).
/// The counter file lives at <c>&lt;workspaceRoot&gt;/artifacts/integration-harness/.counter</c>
/// and is read+written under <see cref="FileShare.None"/> so concurrent harness
/// invocations from the same workspace queue rather than collide (spec §6 risk
/// register: "<c>&lt;run-id&gt;</c> collision under parallel invocation").
/// The temp root is resolved via <c>TMPDIR</c> (Linux/macOS convention),
/// then <c>TEMP</c> (Windows fallback), then the literal <c>/tmp</c> — never via
/// <see cref="System.IO.Path.GetTempPath"/> which is on <c>BannedSymbols.txt</c>.
/// </summary>
public static class ScratchDir
{
    /// <summary>
    /// Allocates the next scratch directory for <paramref name="subcommandName"/>
    /// and returns its absolute path. The directory is freshly created (an existing
    /// stale directory is deleted first so no artefacts leak between runs).
    /// </summary>
    public static string For(string subcommandName, string workspaceRoot)
    {
        var counterPath = Path.Combine(
            workspaceRoot, "artifacts", "integration-harness", ".counter");
        Directory.CreateDirectory(Path.GetDirectoryName(counterPath)!);

        int next;
        using (var fs = new FileStream(
            counterPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var reader = new StreamReader(fs, leaveOpen: true))
        {
            var text = reader.ReadToEnd();
            var current = int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var n) ? n : 0;
            next = current + 1;
            fs.SetLength(0);
            fs.Position = 0;
            using var writer = new StreamWriter(fs, leaveOpen: true);
            writer.Write(next.ToString(CultureInfo.InvariantCulture));
        }

        var tmp = GetTempRoot();
        var dir = Path.Combine(tmp, $"gravity-{subcommandName}-run{next}");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Resolves the temp root without using the banned <c>Path.GetTempPath()</c>.
    /// </summary>
    private static string GetTempRoot()
        => Environment.GetEnvironmentVariable("TMPDIR")
           ?? Environment.GetEnvironmentVariable("TEMP")
           ?? "/tmp";
}
