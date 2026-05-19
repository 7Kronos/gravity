using System;
using System.IO;
using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Xunit;

namespace Gravity.Dsl.IntegrationHarness.Tests;

/// <summary>
/// FR-3000: argv routing — unknown tokens must exit non-zero and name all legal subcommands.
/// </summary>
public sealed class SubcommandDispatchTests
{
    [Fact]
    public void UnknownToken_ExitsNonZero()
    {
        var repoRoot = FindRepoRoot();
        var harnessCsproj = Path.Combine(
            repoRoot, "Gravity.Dsl.IntegrationHarness",
            "Gravity.Dsl.IntegrationHarness.csproj");

        var (exit, _, stderr) = ProcessRunner.RunDotnetCapture(
            "run --project \"" + harnessCsproj + "\" -- garbage-token",
            repoRoot);

        exit.Should().NotBe(0,
            because: "FR-3000: an unknown subcommand token must cause a non-zero exit");

        // stderr must mention all seven legal subcommand values
        stderr.Should().Contain("run-ac-9.7-pack", because: "FR-3000: usage message must list run-ac-9.7-pack");
        stderr.Should().Contain("run-ac-9.11",     because: "FR-3000: usage message must list run-ac-9.11");
        stderr.Should().Contain("run-ac-9.12",     because: "FR-3000: usage message must list run-ac-9.12");
        stderr.Should().Contain("run-ac-9.13",     because: "FR-3000: usage message must list run-ac-9.13");
        stderr.Should().Contain("run-ac-9.14",     because: "FR-3000: usage message must list run-ac-9.14");
        stderr.Should().Contain("run-ac-9.15",     because: "FR-3000: usage message must list run-ac-9.15");
        stderr.Should().Contain("run-all",         because: "FR-3000: usage message must list run-all");
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
}
