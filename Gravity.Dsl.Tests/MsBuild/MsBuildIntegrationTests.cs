using FluentAssertions;
using Gravity.Dsl.IntegrationHarness.Shared;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.MsBuild;

/// <summary>
/// Phase 9c wrapper Facts for AC-9.11..AC-9.15. Each Fact shells out to the
/// integration harness subcommand via <see cref="HarnessInvoker.Run"/>, which
/// preserves the cross-process boundary that is the motivation for Phase 9c
/// (FR-3004 / plan.md §3.5). The shared invoker sets
/// <c>DOTNET_CLI_USE_MSBUILD_SERVER=0</c> on the child to prevent MSBuild
/// server lock contention with the xUnit test host.
/// </summary>
[Trait("Category", "Slow")]
public sealed class MsBuildIntegrationTests
{
    [Fact]
    public void AC_9_11_ItemMetadataOverride() =>
        RunSubcommand("run-ac-9.11", "AC-9.11 PASS");

    [Fact]
    public void AC_9_12_HookOrder() =>
        RunSubcommand("run-ac-9.12", "AC-9.12 PASS");

    [Fact]
    public void AC_9_13_EmptyInput() =>
        RunSubcommand("run-ac-9.13", "AC-9.13 PASS");

    [Fact]
    public void AC_9_14_NoGlobalTool() =>
        RunSubcommand("run-ac-9.14", "AC-9.14 PASS");

    [Fact]
    public void AC_9_15_IncrementalBuild() =>
        RunSubcommand("run-ac-9.15", "AC-9.15 PASS");

    private static void RunSubcommand(string subcommand, string expectedPassMarker)
    {
        var msbuildDir = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.MsBuild");
        var repoRoot = new DirectoryInfo(msbuildDir).Parent!.FullName;
        var (exit, stdout, stderr) = HarnessInvoker.Run(subcommand, repoRoot);
        exit.Should().Be(0,
            because: "harness subcommand " + subcommand + " must pass.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
        stdout.Should().Contain(expectedPassMarker,
            because: "stdout must contain " + expectedPassMarker);
    }
}
