using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.MsBuild;

/// <summary>
/// Phase 9c wrapper Facts for AC-9.11..AC-9.15. Each Fact shells out to the
/// integration harness subcommand via <c>dotnet run --project
/// Gravity.Dsl.IntegrationHarness</c>, preserving the cross-process boundary
/// that is the motivation for Phase 9c (FR-3004 / plan.md §3.5).
/// <c>DOTNET_CLI_USE_MSBUILD_SERVER=0</c> is set on the child process to
/// prevent MSBuild server lock contention with the xUnit test host.
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
        var (exit, stdout, stderr) = RunHarness(subcommand, repoRoot);
        exit.Should().Be(0,
            because: "harness subcommand " + subcommand + " must pass.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
        stdout.Should().Contain(expectedPassMarker,
            because: "stdout must contain " + expectedPassMarker);
    }

    /// <summary>
    /// Invokes <c>dotnet run --project Gravity.Dsl.IntegrationHarness -- &lt;subcommand&gt;</c>
    /// with <c>DOTNET_CLI_USE_MSBUILD_SERVER=0</c> to prevent MSBuild server
    /// lock contention when running inside the xUnit test host process.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunHarness(
        string subcommand, string repoRoot)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project Gravity.Dsl.IntegrationHarness -- " + subcommand,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Disable MSBuild server to avoid lock contention with the xUnit host.
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["DOTNET_BUILD_SERVER_AUTOSTART"] = "0";
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(600_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new InvalidOperationException("harness timed out: " + subcommand);
        }
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
