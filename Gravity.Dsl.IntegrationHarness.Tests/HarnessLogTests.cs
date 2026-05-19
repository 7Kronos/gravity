using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Xunit;

namespace Gravity.Dsl.IntegrationHarness.Tests;

/// <summary>
/// FR-3033 / AC-9c.8: stdout shape and JUnit XML determinism tests using
/// stub <see cref="ISubcommand"/> implementations that return synthetic results
/// without actually running dotnet build/pack.
/// </summary>
public sealed class HarnessLogTests
{
    [Fact]
    public void RunAll_Success_FinalLineMatches()
    {
        var (repoRoot, tmpBase) = SetupTmp("log-success");
        try
        {
            var outDir = Path.Combine(tmpBase, "artifacts");
            Directory.CreateDirectory(outDir);

            var opts = HarnessOptions.Parse(new[] { "run-all", "--out", outDir });
            var runner = new HarnessRunner(opts, repoRoot);
            var subcommands = BuildStubSubcommands(pass: true);

            // Capture stdout
            var origOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var exit = runner.RunAll(subcommands);
                exit.Should().Be(0, because: "all stubs pass");
            }
            finally
            {
                Console.SetOut(origOut);
            }

            var stdout = sw.ToString();
            stdout.Should().Contain("Phase 9c integration harness: 6/6 steps passed.",
                because: "AC-9c.4: run-all final line must match exactly");
        }
        finally
        {
            Cleanup(tmpBase);
        }
    }

    [Fact]
    public void RunAll_TwiceAgainstCleanWorkspace_JunitXmlByteEqual()
    {
        var (repoRoot, tmpBase) = SetupTmp("log-determinism");
        try
        {
            var repoRoot2 = repoRoot;

            void RunOnce(string outDir)
            {
                // Clean-then-recreate the out dir (and counter) to reset run-id to 1.
                if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
                Directory.CreateDirectory(outDir);

                // Also wipe the counter file so sequence numbers reset.
                var counterDir = Path.Combine(repoRoot2, "artifacts", "integration-harness");
                var counterFile = Path.Combine(counterDir, ".counter");
                if (File.Exists(counterFile)) File.Delete(counterFile);

                var opts = HarnessOptions.Parse(new[] { "run-all", "--out", outDir });
                var runner = new HarnessRunner(opts, repoRoot2);
                var subcommands = BuildStubSubcommands(pass: true);

                var origOut = Console.Out;
                Console.SetOut(System.IO.TextWriter.Null);
                try { runner.RunAll(subcommands); }
                finally { Console.SetOut(origOut); }
            }

            var outDir1 = Path.Combine(tmpBase, "run1-artifacts");
            var outDir2 = Path.Combine(tmpBase, "run2-artifacts");

            RunOnce(outDir1);
            RunOnce(outDir2);

            var junit1 = Path.Combine(outDir1, "junit.xml");
            var junit2 = Path.Combine(outDir2, "junit.xml");

            File.Exists(junit1).Should().BeTrue(because: "junit.xml must be written after first run");
            File.Exists(junit2).Should().BeTrue(because: "junit.xml must be written after second run");

            var hash1 = Sha256File(junit1);
            var hash2 = Sha256File(junit2);
            hash1.Should().Be(hash2,
                because: "AC-9c.8: two consecutive run-all invocations from a clean workspace must produce byte-identical junit.xml");
        }
        finally
        {
            Cleanup(tmpBase);
        }
    }

    // ---- helpers ----

    private static (string RepoRoot, string TmpBase) SetupTmp(string label)
    {
        var tmp = Environment.GetEnvironmentVariable("TMPDIR")
                  ?? Environment.GetEnvironmentVariable("TEMP")
                  ?? "/tmp";
        var tmpBase = Path.Combine(tmp, "gravity-harness-log-tests-" + label);
        if (Directory.Exists(tmpBase)) Directory.Delete(tmpBase, recursive: true);
        Directory.CreateDirectory(tmpBase);
        var repoRoot = FindRepoRoot();
        return (repoRoot, tmpBase);
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private static string Sha256File(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Gravity.Dsl.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Gravity.Dsl.sln from: " + AppContext.BaseDirectory);
    }

    /// <summary>Builds six stub subcommands returning synthetic results.</summary>
    private static IReadOnlyList<Gravity.Dsl.IntegrationHarness.Subcommands.ISubcommand> BuildStubSubcommands(bool pass)
    {
        return new List<Gravity.Dsl.IntegrationHarness.Subcommands.ISubcommand>
        {
            new StubSubcommand("run-ac-9.7-pack", "9.7-pack", pass),
            new StubSubcommand("run-ac-9.11",     "9.11",     pass),
            new StubSubcommand("run-ac-9.12",     "9.12",     pass),
            new StubSubcommand("run-ac-9.13",     "9.13",     pass),
            new StubSubcommand("run-ac-9.14",     "9.14",     pass),
            new StubSubcommand("run-ac-9.15",     "9.15",     pass),
        };
    }

    private sealed class StubSubcommand : Gravity.Dsl.IntegrationHarness.Subcommands.ISubcommand
    {
        private readonly bool _pass;
        public string SubcommandName { get; }
        public string AcId { get; }

        public StubSubcommand(string name, string acId, bool pass)
        {
            SubcommandName = name;
            AcId = acId;
            _pass = pass;
        }

        public Gravity.Dsl.IntegrationHarness.Subcommands.SubcommandResult Run(
            string scratchDir, string workspaceRoot, HarnessLog log, string config)
        {
            log.WriteToFile("[stub] " + SubcommandName + " called; config=" + config);
            return _pass
                ? Gravity.Dsl.IntegrationHarness.Subcommands.SubcommandResult.Pass()
                : Gravity.Dsl.IntegrationHarness.Subcommands.SubcommandResult.Fail(
                    HarnessRuleIds.Harn010, "stub failure for " + SubcommandName);
        }
    }
}
