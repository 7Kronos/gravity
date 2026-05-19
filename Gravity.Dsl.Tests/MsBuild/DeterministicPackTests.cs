using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.MsBuild;

/// <summary>
/// FR-212 / AC-9.7-pack wrapper. Shells out to the integration harness
/// <c>run-ac-9.7-pack</c> subcommand which packs twice, normalises both
/// outputs, and asserts byte equality (FR-3015 / plan.md §3.5).
/// <c>DOTNET_CLI_USE_MSBUILD_SERVER=0</c> is set on the child process to
/// prevent MSBuild server lock contention with the xUnit test host.
/// </summary>
[Trait("Category", "Slow")]
public sealed class DeterministicPackTests
{
    [Fact]
    public void AC_9_7_Pack_PackDeterminism()
    {
        var msbuildDir = SamplesLoader.FindRepoSubdirectory("Gravity.Dsl.MsBuild");
        var repoRoot = new DirectoryInfo(msbuildDir).Parent!.FullName;
        var (exit, stdout, stderr) = RunHarness("run-ac-9.7-pack", repoRoot);
        exit.Should().Be(0,
            because: "harness subcommand run-ac-9.7-pack must pass.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
        stdout.Should().Contain("AC-9.7-pack PASS",
            because: "stdout must contain AC-9.7-pack PASS");
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
