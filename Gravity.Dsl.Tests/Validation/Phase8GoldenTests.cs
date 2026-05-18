using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Validation;
using Xunit;

namespace Gravity.Dsl.Tests.Validation;

/// <summary>
/// Phase 8 / AC-8.15. Byte-checked golden file for the combined fixture that
/// exercises every Phase 8 rule (VAL020..VAL030). Pins both
/// <list type="bullet">
///   <item>FR-160 diagnostic ordering — the emitted byte sequence is the
///     <c>DiagnosticSink.Compare</c> output and any sort regression breaks the
///     golden;</item>
///   <item>FR-130 / FR-150 VAL020 message wording — wording changes flow through
///     to the golden and require a deliberate update.</item>
/// </list>
/// <para>
/// To regenerate the golden after an intended message-format change, run:
/// </para>
/// <code>UPDATE_GOLDEN=1 dotnet test --filter "FullyQualifiedName~Phase8GoldenTests"</code>
/// <para>
/// Mirrors the T048-era golden mechanism used by the C# emitter goldens. LF
/// line endings, UTF-8 no BOM.
/// </para>
/// </summary>
public sealed class Phase8GoldenTests
{
    private static readonly HashSet<string> ClaimedNamespaces =
        new(StringComparer.Ordinal) { "csharp" };

    private static readonly DateOnly GoldenDate = new(2026, 5, 18);

    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "versioning", "validator");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/versioning/validator not found");
    }

    private static string GoldenDir()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "golden", "diagnostics", "phase8");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/golden/diagnostics/phase8 not found");
    }

    private static string FormatDiagnostic(Diagnostic d)
    {
        var sev = d.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "info"
        };
        // Format mirrors Gravity.Dsl.Cli.DiagnosticFormatter (which is internal
        // to the CLI assembly). Keeping the format here in sync is enforced by
        // the AC-7 CLI tests; if those formats drift, both tests will surface
        // the regression.
        return d.Span.Path + ":" + d.Span.Line + ":" + d.Span.Column + ": "
            + sev + " " + d.RuleId + ": " + d.Message;
    }

    private static IReadOnlyList<Diagnostic> RunValidatorOnCombinedFixture(out string fixturePath)
    {
        fixturePath = Path.Combine(FixtureRoot(), "combined_all_rules.gravity");
        var src = File.ReadAllText(fixturePath);
        var parsed = Parser.Parse(fixturePath, src);
        parsed.Diagnostics.Should().BeEmpty(because: "combined fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, FixtureRoot());
        resolve.Model.Should().NotBeNull(because: "combined fixture must resolve cleanly");
        return Validator.Validate(resolve.Model!, ClaimedNamespaces, GoldenDate);
    }

    private static string FormatPhase8Block(IReadOnlyList<Diagnostic> diags, string fixturePath)
    {
        // Filter to the Phase 8 block (VAL020..VAL030) so emitter-host or
        // Phase 0–3 changes do not perturb this golden.
        var phase8 = diags
            .Where(d => string.CompareOrdinal(d.RuleId, "VAL020") >= 0
                     && string.CompareOrdinal(d.RuleId, "VAL031") < 0)
            .ToList();
        phase8.Should().NotBeEmpty(because: "combined fixture must produce at least one Phase 8 diagnostic");

        var sb = new StringBuilder();
        foreach (var d in phase8)
        {
            // Normalise the absolute fixture path to a stable repo-relative form
            // so the golden bytes do not depend on where the repo sits on disk.
            var formatted = FormatDiagnostic(d).Replace(fixturePath,
                "tests/fixtures/versioning/validator/combined_all_rules.gravity",
                StringComparison.Ordinal);
            sb.Append(formatted).Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public void CombinedFixture_Phase8Block_MatchesGoldenByteForByte()
    {
        var diags = RunValidatorOnCombinedFixture(out var fixturePath);
        var actual = FormatPhase8Block(diags, fixturePath);

        var goldenPath = Path.Combine(GoldenDir(), "combined.txt");

        // Update mode: write the current output back to the golden file.
        // Triggered by env var UPDATE_GOLDEN=1 (mirrors the T048 mechanism on
        // the C# emitter goldens).
        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_GOLDEN"), "1",
                StringComparison.Ordinal))
        {
            File.WriteAllText(goldenPath, actual);
            // Still assert equality so the test passes deterministically in the
            // same run — the file we just wrote must equal what we produced.
            File.ReadAllText(goldenPath).Replace("\r\n", "\n")
                .Should().Be(actual, because: "update mode: golden must reflect actual output");
            return;
        }

        File.Exists(goldenPath).Should().BeTrue(
            because: "tests/golden/diagnostics/phase8/combined.txt must be checked in; "
                  + "run with UPDATE_GOLDEN=1 to regenerate");
        var expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
        actual.Should().Be(expected,
            because: "AC-8.15: Phase 8 combined-fixture diagnostics must match the byte-checked golden. "
                  + "Run with UPDATE_GOLDEN=1 after a deliberate message-format change.");
    }
}
