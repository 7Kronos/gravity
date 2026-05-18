using System;
using System.Collections.Generic;
using System.Linq;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Cli;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace Gravity.Dsl.MsBuild;

/// <summary>
/// MSBuild task driving Gravity DSL codegen (FR-200, FR-202, FR-204, FR-205,
/// FR-230..FR-235, FR-240..FR-242; plan.md §3.3). Thin wrapper over the public
/// <see cref="CompilerPipeline"/> entry point so behaviour is identical to the
/// gravc CLI (LD-11 — build-integration parity).
/// </summary>
/// <remarks>
/// The <c>MsBuildTask</c> alias for <c>Microsoft.Build.Utilities.Task</c> avoids
/// the CS0104 collision with <c>System.Threading.Tasks.Task</c> brought in by
/// the project's global usings.
/// </remarks>
public sealed class GravityDslGenTask : MsBuildTask
{
    /// <summary>Source .gravity files supplied via the <c>&lt;GravityDsl&gt;</c> item type.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Root directory generated artefacts are written under (FR-251).</summary>
    [Required]
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>Explicit path to a <c>.gravity.config</c> file (FR-203); empty falls back to default discovery.</summary>
    public string ConfigFile { get; set; } = string.Empty;

    /// <summary><c>--as-of YYYY-MM-DD</c> equivalent (FR-233); empty defaults to today (UTC).</summary>
    public string AsOf { get; set; } = string.Empty;

    /// <summary>Absolute path to <c>$(MSBuildProjectDirectory)</c>; used to resolve relative metadata.</summary>
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Optional list of extra emitter assemblies (FR-224) — each <c>ITaskItem</c>'s
    /// <c>FullPath</c> names a <c>.dll</c> exposing one or more <c>IEmitter</c> types.
    /// Populated by <c>@(GravityDslEmitterAssembly)</c> items contributed by emitter
    /// packages (e.g. <c>Gravity.Dsl.Emitter.Sample.Outline</c>) whose
    /// <c>buildTransitive/</c> props file adds the relevant DLL path.
    /// </summary>
    public ITaskItem[] EmitterAssemblies { get; set; } = Array.Empty<ITaskItem>();

    /// <inheritdoc />
    public override bool Execute()
    {
        if (!TryResolveAsOf(AsOf, out var currentDate))
        {
            return false;
        }

        if (Sources.Length == 0)
        {
            // The targets file already conditions GravityDslGenerate on '@(GravityDsl)' != '';
            // an empty Sources array means the consumer passed a literal empty list. Nothing to do.
            return true;
        }

        // FR-203: validate the optional explicit config path up-front so the failure
        // surfaces before any compilation work begins.
        string? configFile = null;
        if (!string.IsNullOrEmpty(ConfigFile))
        {
            var resolved = ResolveAgainstProjectDir(ConfigFile);
            if (!System.IO.File.Exists(resolved))
            {
                Log.LogError(
                    subcategory: null,
                    errorCode: MsBuildRuleIds.Msb003,
                    helpKeyword: null,
                    file: GetProjectFilePath(),
                    lineNumber: 1, columnNumber: 1, endLineNumber: 0, endColumnNumber: 0,
                    message: "<GravityDslConfig> file does not exist: " + resolved);
                return false;
            }
            configFile = resolved;
        }

        // FR-224: resolve extra emitter assembly paths once up-front; each path is
        // threaded into every per-group CompilerPipeline.Gen call so an emitter
        // package's contribution is visible to every <GravityDsl> group.
        IReadOnlyList<string>? extraEmitterAssemblies = null;
        if (EmitterAssemblies.Length > 0)
        {
            extraEmitterAssemblies = EmitterAssemblies
                .Select(i => i.GetMetadata("FullPath"))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        // FR-202: group inputs by (resolved Output override, Emitter whitelist) so per-item
        // metadata actually flows through to the emitter host. Items without metadata share
        // the task-level OutputDir / no-emitter-filter group.
        var groups = Sources
            .GroupBy(item => (
                Output: ResolveOutputMetadata(item),
                Emitter: item.GetMetadata("Emitter") ?? string.Empty))
            .OrderBy(g => g.Key.Output, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Emitter, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var sources = group.Select(i => i.GetMetadata("FullPath")).ToList();
            IReadOnlyList<string>? emitterFilter = null;
            if (!string.IsNullOrEmpty(group.Key.Emitter))
            {
                emitterFilter = group.Key.Emitter
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
            }

            // FR-230: InProc invocation via the shared public CompilerPipeline.Gen surface.
            var result = CompilerPipeline.Gen(
                inputs: sources,
                outputRoot: group.Key.Output,
                currentDate: currentDate,
                configFile: configFile,
                emitterFilter: emitterFilter,
                extraEmitterAssemblies: extraEmitterAssemblies).GetAwaiter().GetResult();

            foreach (var d in result.Diagnostics)
            {
                LogDiagnostic(d);
            }

            // FR-205 / FR-231: any error severity diagnostic fails the build.
            if (Log.HasLoggedErrors)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// FR-206 / FR-240 / FR-241: map a <see cref="Diagnostic"/> to MSBuild's canonical
    /// <c>path(line,col): severity ruleId: message</c> surface via the positional Log API.
    /// Letting the Log API render the form is what guarantees byte-identical IDE click-through
    /// behaviour across Rider, VS, and VS Code.
    /// </summary>
    private void LogDiagnostic(Diagnostic d)
    {
        var path = d.Span.Path;
        var line = d.Span.Line;
        var column = d.Span.Column;

        switch (d.Severity)
        {
            case DiagnosticSeverity.Error:
                Log.LogError(
                    subcategory: null,
                    errorCode: d.RuleId,
                    helpKeyword: null,
                    file: path,
                    lineNumber: line, columnNumber: column,
                    endLineNumber: 0, endColumnNumber: 0,
                    message: d.Message);
                break;
            case DiagnosticSeverity.Warning:
                Log.LogWarning(
                    subcategory: null,
                    warningCode: d.RuleId,
                    helpKeyword: null,
                    file: path,
                    lineNumber: line, columnNumber: column,
                    endLineNumber: 0, endColumnNumber: 0,
                    message: d.Message);
                break;
            default:
                // FR-241: Info severity loses the rule-id surface; MSBuild messages have no code field.
                Log.LogMessage(MessageImportance.Low, "{0}: {1}", d.RuleId, d.Message);
                break;
        }
    }

    /// <summary>
    /// FR-233: parse the <c>&lt;GravityDslAsOf&gt;</c> property; empty defaults to today (UTC).
    /// Delegates to <see cref="Cli.MsBuildDateResolver.TryResolve"/> — the shared helper
    /// used by both the CLI and the MSBuild task (plan.md §3.8). The helper is the one
    /// allowed <see cref="DateTime.UtcNow"/> call site on the build side
    /// (banned-API analyzer carve-out per Directory.Build.props).
    /// Emits MSB001 on malformed input.
    /// </summary>
    private bool TryResolveAsOf(string raw, out DateOnly asOf)
    {
        if (!Cli.MsBuildDateResolver.TryResolve(raw, out asOf, out var error))
        {
            Log.LogError(
                subcategory: null,
                errorCode: MsBuildRuleIds.Msb001,
                helpKeyword: null,
                file: GetProjectFilePath(),
                lineNumber: 1, columnNumber: 1, endLineNumber: 0, endColumnNumber: 0,
                message: "<GravityDslAsOf> " + error);
            return false;
        }
        return true;
    }

    private string ResolveOutputMetadata(ITaskItem item)
    {
        var raw = item.GetMetadata("Output");
        if (string.IsNullOrEmpty(raw)) return OutputDir;
        return ResolveAgainstProjectDir(raw);
    }

    private string ResolveAgainstProjectDir(string path)
    {
        if (System.IO.Path.IsPathRooted(path)) return System.IO.Path.GetFullPath(path);
        var baseDir = string.IsNullOrEmpty(ProjectDirectory)
            ? System.IO.Directory.GetCurrentDirectory()
            : ProjectDirectory;
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
    }

    private string GetProjectFilePath()
    {
        // BuildEngine may expose the project file path; fall back to ProjectDirectory.
        if (BuildEngine is { } engine && !string.IsNullOrEmpty(engine.ProjectFileOfTaskNode))
        {
            return engine.ProjectFileOfTaskNode;
        }
        return string.IsNullOrEmpty(ProjectDirectory) ? string.Empty : ProjectDirectory;
    }
}
