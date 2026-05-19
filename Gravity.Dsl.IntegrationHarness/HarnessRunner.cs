using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Gravity.Dsl.IntegrationHarness.Shared;
using Gravity.Dsl.IntegrationHarness.Subcommands;

namespace Gravity.Dsl.IntegrationHarness;

/// <summary>
/// Orchestrates subcommand execution, per-step log files, JUnit XML output,
/// and the final summary line (FR-3000, FR-3033, AC-9c.4, AC-9c.8).
/// </summary>
public sealed class HarnessRunner
{
    private readonly HarnessOptions _opts;
    private readonly string _repoRoot;

    public HarnessRunner(HarnessOptions opts, string repoRoot)
    {
        _opts = opts;
        _repoRoot = repoRoot;
    }

    /// <summary>
    /// Runs all subcommands in declaration order, writes per-step logs and
    /// a <c>junit.xml</c> summary, emits the FR-3033 stdout shape.
    /// Returns 0 on full pass; the count of failed subcommands on partial failure.
    /// </summary>
    public int RunAll(IReadOnlyList<ISubcommand> subcommands)
    {
        var outDir = _opts.ResolveOutDir(_repoRoot);
        Directory.CreateDirectory(outDir);

        // FR-3000: apply --filter as an ordinal, case-sensitive substring match against
        // SubcommandName. Substring (not exact-token) is deliberate so '--filter 9.1' can
        // select the 9.11..9.15 family; pass the full subcommand name for an exact match.
        // Only meaningful in run-all mode; the run-one path bypasses RunAll entirely.
        var effective = subcommands;
        if (!string.IsNullOrEmpty(_opts.Filter))
        {
            var filter = _opts.Filter;
            var filtered = new List<ISubcommand>();
            foreach (var s in subcommands)
            {
                if (s.SubcommandName.Contains(filter, StringComparison.Ordinal))
                    filtered.Add(s);
            }
            if (filtered.Count == 0)
            {
                Console.Error.Write("[harness] ERROR: --filter '" + filter
                    + "' excluded every subcommand. Available subcommand names: "
                    + string.Join(", ", subcommands.Select(s => s.SubcommandName)) + "\n");
                return 2;
            }
            effective = filtered;
        }

        var results = new List<(ISubcommand Sub, SubcommandResult Result, string LogPath)>();

        foreach (var sub in effective)
        {
            var scratchDir = ScratchDir.For(sub.SubcommandName, _repoRoot);
            var logPath = Path.Combine(outDir, sub.SubcommandName.Replace("/", "-", StringComparison.Ordinal) + ".log");

            using var log = new HarnessLog(logPath);
            SubcommandResult result;
            try
            {
                result = sub.Run(scratchDir, _repoRoot, log, _opts.Config);
            }
            catch (Exception ex)
            {
                log.WriteToFile("[harness] EXCEPTION: " + ex);
                result = SubcommandResult.Fail(
                    HarnessRuleIds.Harn010,
                    "Unhandled exception in subcommand " + sub.SubcommandName + ": " + ex.Message,
                    scratchDir);
            }

            results.Add((sub, result, logPath));

            if (result.Success)
            {
                HarnessLog.EmitPassToStdout(sub.AcId);
            }
            else
            {
                HarnessLog.EmitFailureToStdout(
                    result.HarnessRuleId ?? HarnessRuleIds.Harn010,
                    sub.AcId,
                    result.FixturePath ?? scratchDir,
                    result.DotnetExitCode,
                    logPath);
            }
        }

        WriteJunitXml(outDir, results);

        var passCount = 0;
        var failCount = 0;
        foreach (var (_, result, _) in results)
        {
            if (result.Success) passCount++;
            else failCount++;
        }

        var total = results.Count;
        if (failCount == 0)
        {
            Console.Write("Phase 9c integration harness: " + total.ToString(CultureInfo.InvariantCulture)
                + "/" + total.ToString(CultureInfo.InvariantCulture) + " steps passed.\n");
            return 0;
        }
        else
        {
            Console.Write("Phase 9c integration harness: " + passCount.ToString(CultureInfo.InvariantCulture)
                + "/" + total.ToString(CultureInfo.InvariantCulture) + " steps passed.\n");
            return failCount;
        }
    }

    /// <summary>
    /// Runs a single subcommand. Returns 0 on pass, 1 on fail.
    /// </summary>
    public int RunOne(ISubcommand sub)
    {
        var outDir = _opts.ResolveOutDir(_repoRoot);
        Directory.CreateDirectory(outDir);

        var scratchDir = ScratchDir.For(sub.SubcommandName, _repoRoot);
        var logPath = Path.Combine(outDir, sub.SubcommandName.Replace("/", "-", StringComparison.Ordinal) + ".log");

        using var log = new HarnessLog(logPath);
        SubcommandResult result;
        try
        {
            result = sub.Run(scratchDir, _repoRoot, log, _opts.Config);
        }
        catch (Exception ex)
        {
            log.WriteToFile("[harness] EXCEPTION: " + ex);
            result = SubcommandResult.Fail(
                HarnessRuleIds.Harn010,
                "Unhandled exception in subcommand " + sub.SubcommandName + ": " + ex.Message,
                scratchDir);
        }

        if (result.Success)
        {
            HarnessLog.EmitPassToStdout(sub.AcId);
            return 0;
        }
        else
        {
            HarnessLog.EmitFailureToStdout(
                result.HarnessRuleId ?? HarnessRuleIds.Harn010,
                sub.AcId,
                result.FixturePath ?? scratchDir,
                result.DotnetExitCode,
                logPath);
            return 1;
        }
    }

    private static void WriteJunitXml(
        string outDir,
        List<(ISubcommand Sub, SubcommandResult Result, string LogPath)> results)
    {
        var xmlPath = Path.Combine(outDir, "junit.xml");
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using var stream = new FileStream(xmlPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = XmlWriter.Create(stream, settings);

        var passCount = 0;
        var failCount = 0;
        foreach (var (_, r, _) in results)
        {
            if (r.Success) passCount++;
            else failCount++;
        }

        writer.WriteStartDocument();
        writer.WriteStartElement("testsuites");
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "Phase 9c Integration Harness");
        writer.WriteAttributeString("tests", results.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("failures", failCount.ToString(CultureInfo.InvariantCulture));

        foreach (var (sub, result, logPath) in results)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("name", sub.SubcommandName);
            writer.WriteAttributeString("classname", "AC-" + sub.AcId);

            if (!result.Success)
            {
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("message",
                    result.HarnessRuleId + ": " + result.FailureMessage);
                writer.WriteString("log: " + logPath);
                writer.WriteEndElement(); // failure
            }

            writer.WriteEndElement(); // testcase
        }

        writer.WriteEndElement(); // testsuite
        writer.WriteEndElement(); // testsuites
        writer.WriteEndDocument();
    }
}
