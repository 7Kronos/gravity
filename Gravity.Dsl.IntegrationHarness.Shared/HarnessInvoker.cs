using System.Diagnostics;

namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Shared <c>dotnet run --project Gravity.Dsl.IntegrationHarness</c> invoker
/// for the xUnit wrappers under <c>Gravity.Dsl.Tests/MsBuild/</c>. Centralises
/// the MSBuild-server suppression and concurrent stdout/stderr drain that both
/// <c>MsBuildIntegrationTests</c> and <c>DeterministicPackTests</c> previously
/// duplicated verbatim.
/// </summary>
public static class HarnessInvoker
{
    private const int DefaultTimeoutMs = 600_000;

    /// <summary>
    /// Runs the integration harness as a child process with the given
    /// <paramref name="subcommand"/>. <c>DOTNET_CLI_USE_MSBUILD_SERVER=0</c> and
    /// <c>DOTNET_BUILD_SERVER_AUTOSTART=0</c> are set on the child to prevent
    /// MSBuild server lock contention with the xUnit test host.
    /// </summary>
    public static (int ExitCode, string Stdout, string Stderr) Run(
        string subcommand, string repoRoot)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Pass argv components individually via ArgumentList rather than
        // composing a single Arguments string, so any future caller passing a
        // value with whitespace or shell metacharacters cannot influence
        // argument parsing (defence in depth — current callers pass literals).
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add("Gravity.Dsl.IntegrationHarness");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(subcommand);
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["DOTNET_BUILD_SERVER_AUTOSTART"] = "0";

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(DefaultTimeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            // Drain output pipes with a short timeout so the tasks fault cleanly
            // rather than dangling against the killed child's broken pipes.
            try { Task.WhenAll(stdoutTask, stderrTask).Wait(2000); } catch { /* best effort */ }
            throw new InvalidOperationException("harness timed out: " + subcommand);
        }
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}
