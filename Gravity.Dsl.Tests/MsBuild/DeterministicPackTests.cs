using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.MsBuild;

/// <summary>
/// FR-212 / AC-9.7-pack wrapper. Shells out via <see cref="HarnessInvoker.Run"/>
/// to the <c>run-ac-9.7-pack</c> subcommand which packs twice, normalises both
/// outputs, and asserts byte equality (FR-3015 / plan.md §3.5).
/// </summary>
[Trait("Category", "Slow")]
public sealed class DeterministicPackTests
{
    [Fact]
    public void AC_9_7_Pack_PackDeterminism()
    {
        var msbuildDir = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.MsBuild");
        var repoRoot = new DirectoryInfo(msbuildDir).Parent!.FullName;
        var (exit, stdout, stderr) = HarnessInvoker.Run("run-ac-9.7-pack", repoRoot);
        exit.Should().Be(0,
            because: "harness subcommand run-ac-9.7-pack must pass.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
        stdout.Should().Contain("AC-9.7-pack PASS",
            because: "stdout must contain AC-9.7-pack PASS");
    }
}
