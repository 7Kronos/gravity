using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;

namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Shared process-runner helpers used by both the xUnit fast lane
/// (<c>Gravity.Dsl.Tests</c>) and the integration harness slow lane
/// (<c>Gravity.Dsl.IntegrationHarness</c>). Concurrent stdout/stderr drain
/// is the load-bearing detail: reading one stream synchronously while the
/// child fills the other can stall once the OS pipe buffer (~4 KB) fills.
/// <see cref="RunDotnetCapture"/> drains both streams concurrently via
/// <c>Task.WhenAll</c> to prevent that classic deadlock.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Runs <c>dotnet <paramref name="args"/></c> in <paramref name="workingDir"/>,
    /// captures stdout and stderr concurrently, and returns all three: exit code,
    /// stdout text, stderr text. Never throws on non-zero exit — the caller decides
    /// what to do with the exit code.
    /// </summary>
    public static (int ExitCode, string Stdout, string Stderr) RunDotnetCapture(
        string args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(300_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new InvalidOperationException("dotnet timed out: " + args);
        }
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    /// <summary>
    /// Runs <c>dotnet <paramref name="args"/></c> in <paramref name="workingDir"/>
    /// and throws (via FluentAssertions) on non-zero exit, surfacing the full
    /// stdout and stderr in the failure message.
    /// </summary>
    public static void RunDotnet(string args, string workingDir)
    {
        var (exit, stdout, stderr) = RunDotnetCapture(args, workingDir);
        exit.Should().Be(0,
            because: "dotnet " + args + " must succeed.\nstdout:\n" + stdout + "\nstderr:\n" + stderr);
    }
}
