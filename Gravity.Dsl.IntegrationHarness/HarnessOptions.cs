using System;
using System.IO;

namespace Gravity.Dsl.IntegrationHarness;

/// <summary>
/// Parsed harness command-line options (FR-3000).
/// Use <see cref="Parse"/> to construct; do not instantiate directly.
/// </summary>
public sealed class HarnessOptions
{
    private static readonly string[] LegalSubcommands =
    {
        "run-ac-9.7-pack",
        "run-ac-9.11",
        "run-ac-9.12",
        "run-ac-9.13",
        "run-ac-9.14",
        "run-ac-9.15",
        "run-all",
    };

    /// <summary>The leading subcommand token (one of the seven legal values).</summary>
    public string Subcommand { get; private set; } = string.Empty;

    /// <summary>Build configuration; defaults to <c>Release</c>.</summary>
    public string Config { get; private set; } = "Release";

    /// <summary>Output directory for log files and junit.xml; defaults to <c>&lt;repo&gt;/artifacts/integration-harness/</c>.</summary>
    public string OutDir { get; private set; } = string.Empty;

    /// <summary>Optional filter pattern (only meaningful when Subcommand is <c>run-all</c>).</summary>
    public string? Filter { get; private set; }

    private HarnessOptions() { }

    /// <summary>
    /// Parses <paramref name="args"/> and returns a populated <see cref="HarnessOptions"/>.
    /// Writes a usage message to stderr and calls <see cref="Environment.Exit"/>(2) on any error.
    /// </summary>
    public static HarnessOptions Parse(string[] args)
    {
        var opts = new HarnessOptions();

        if (args.Length == 0)
        {
            WriteUsage("No subcommand specified.");
            Environment.Exit(2);
        }

        var subcommand = args[0];
        var found = false;
        foreach (var legal in LegalSubcommands)
        {
            if (string.Equals(subcommand, legal, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            WriteUsage("Unknown subcommand: " + subcommand);
            Environment.Exit(2);
        }
        opts.Subcommand = subcommand;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" or "-c":
                    if (i + 1 >= args.Length)
                    {
                        WriteUsage("--config requires a value (Debug|Release).");
                        Environment.Exit(2);
                    }
                    opts.Config = args[++i];
                    break;

                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        WriteUsage("--out requires a directory path.");
                        Environment.Exit(2);
                    }
                    opts.OutDir = args[++i];
                    break;

                case "--filter":
                    if (i + 1 >= args.Length)
                    {
                        WriteUsage("--filter requires a pattern.");
                        Environment.Exit(2);
                    }
                    opts.Filter = args[++i];
                    break;

                default:
                    WriteUsage("Unknown flag: " + args[i]);
                    Environment.Exit(2);
                    break;
            }
        }

        return opts;
    }

    /// <summary>
    /// Resolves the output directory. If <see cref="OutDir"/> was not set on the
    /// command line, returns the default <c>&lt;repoRoot&gt;/artifacts/integration-harness/</c>.
    /// </summary>
    public string ResolveOutDir(string repoRoot)
    {
        if (!string.IsNullOrEmpty(OutDir))
            return OutDir;
        return Path.Combine(repoRoot, "artifacts", "integration-harness");
    }

    private static void WriteUsage(string error)
    {
        Console.Error.Write("[harness] ERROR: " + error + "\n");
        Console.Error.Write("Usage: dotnet run --project Gravity.Dsl.IntegrationHarness -- <subcommand> [--config Debug|Release] [--out <dir>] [--filter <pattern>]\n");
        Console.Error.Write("Legal subcommands: run-ac-9.7-pack, run-ac-9.11, run-ac-9.12, run-ac-9.13, run-ac-9.14, run-ac-9.15, run-all\n");
    }
}
