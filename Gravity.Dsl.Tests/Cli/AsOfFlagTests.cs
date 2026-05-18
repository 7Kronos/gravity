using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Cli;
using Xunit;

namespace Gravity.Dsl.Tests.Cli;

/// <summary>
/// Phase 8 (T169 / AC-8.11) integration tests for the <c>--as-of</c> flag and
/// the <c>CLI002</c> negative path. Tests invoke <see cref="CompilerPipeline"/>
/// in process (no subprocess spawn) for the positive paths and
/// <see cref="Program.Main"/> in process with redirected stderr for the
/// malformed-flag case.
/// </summary>
public sealed class AsOfFlagTests
{
    private static string FixtureRoot(string sub)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "versioning", "cli_as_of", sub);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/versioning/cli_as_of/" + sub + " not found");
    }

    // (1) In-window — until=2099-12-31 with --as-of=2099-01-01 (before until) → exit 0.
    [Fact]
    public async Task InWindow_AsOf_2099_01_01_Vs_Until_2099_12_31_ExitsSuccess()
    {
        var root = FixtureRoot("in_window");
        var asOf = new DateOnly(2099, 1, 1);
        var result = await CompilerPipeline.Check(root, asOf);
        result.Success.Should().BeTrue(
            because: "until 2099-12-31 is past 2099-01-01; got: "
                + string.Join("; ", result.Diagnostics.Select(d => d.RuleId + " " + d.Message)));
        result.Diagnostics.Should().NotContain(d => d.RuleId == "VAL030");
    }

    // (2) Out-of-window — until=2098-12-31 with --as-of=2099-01-01 (after until) → fail + VAL030.
    [Fact]
    public async Task Expired_AsOf_2099_01_01_Vs_Until_2098_12_31_ExitsFailure_WithVal030()
    {
        var root = FixtureRoot("expired");
        var asOf = new DateOnly(2099, 1, 1);
        var result = await CompilerPipeline.Check(root, asOf);
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.RuleId == "VAL030");
    }

    // (3) Malformed flag — --as-of 2026-13-45 → exit non-zero, CLI002 on stderr.
    [Fact]
    public async Task MalformedAsOf_EmitsCli002_AndExitsNonZero()
    {
        var root = FixtureRoot("in_window");
        var originalErr = Console.Error;
        var originalOut = Console.Out;
        var errBuf = new StringWriter();
        var outBuf = new StringWriter();
        try
        {
            Console.SetError(errBuf);
            Console.SetOut(outBuf);
            int exit = await Program.Main(new[] { "check", "--input", root, "--as-of", "2026-13-45" });
            exit.Should().NotBe(0);
            errBuf.ToString().Should().Contain("CLI002");
            errBuf.ToString().Should().Contain("2026-13-45");
        }
        finally
        {
            Console.SetError(originalErr);
            Console.SetOut(originalOut);
        }
    }

    // (4) No --as-of, fixture until=9999-12-31 → exit 0 (default clock is far before 9999).
    [Fact]
    public async Task NoAsOf_FarFutureUntil_ExitsSuccess()
    {
        var root = FixtureRoot("far_future");
        // Use the same clock-default path the CLI takes, but read it locally here
        // so the test doesn't depend on Program.Main's argument parser.
        var defaultAsOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await CompilerPipeline.Check(root, defaultAsOf);
        result.Success.Should().BeTrue(
            because: "default-clock window check should remain in-window for 9999-12-31; got: "
                + string.Join("; ", result.Diagnostics.Select(d => d.RuleId + " " + d.Message)));
        result.Diagnostics.Should().NotContain(d => d.RuleId == "VAL030");
    }
}
