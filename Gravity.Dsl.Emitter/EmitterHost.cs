using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// Runs a registry of <see cref="IEmitter"/>s against a <see cref="ResolvedModel"/>,
/// invoking enabled emitters in parallel via <see cref="Parallel.ForEachAsync"/>.
/// Output is buffered through <see cref="BufferedEmitterOutput"/> and committed to
/// disk in ordinal relative-path order after each emitter completes. Diagnostics
/// aggregated from emitters are sorted by <c>(Path, Line, Column, RuleId)</c> before
/// propagation so the CLI sees deterministic output regardless of parallel
/// completion order (plan.md §3.6).
/// </summary>
public static class EmitterHost
{
    /// <summary>
    /// Result of <see cref="Run"/>. <see cref="Diagnostics"/> is the sorted union of
    /// the registry's discovery diagnostics, the host's own pre-flight diagnostics,
    /// and the per-emitter <see cref="EmitResult.Diagnostics"/>.
    /// <see cref="EmitterBuffers"/> is keyed by <c>TargetName</c> and exposes each
    /// emitter's in-memory buffer for tests that want to assert on the bytes
    /// without committing to disk.
    /// </summary>
    public sealed record RunResult(
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableSortedDictionary<string, BufferedEmitterOutput> EmitterBuffers);

    /// <summary>
    /// Invoke every enabled emitter in <paramref name="registry"/> with its matching
    /// <see cref="EmitterConfig"/> from <paramref name="configs"/>. When
    /// <paramref name="outputRoot"/> is non-null, each emitter's buffer is committed
    /// to <c>&lt;outputRoot&gt;/&lt;emitter.output&gt;</c>; when null, buffers are
    /// returned only.
    /// </summary>
    public static async Task<RunResult> Run(
        ResolvedModel model,
        IReadOnlyDictionary<string, EmitterConfig> configs,
        EmitterRegistry registry,
        string? outputRoot,
        CancellationToken cancellationToken = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (configs is null) throw new ArgumentNullException(nameof(configs));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        diagnostics.AddRange(registry.Diagnostics);

        // Select emitters that are present in configs AND enabled. Sort by TargetName.
        var selected = new List<(IEmitter Emitter, EmitterConfig Config)>();
        foreach (var e in registry.Emitters)
        {
            if (!configs.TryGetValue(e.TargetName, out var cfg)) continue;
            if (!cfg.Enabled) continue;
            selected.Add((e, cfg));
        }
        selected.Sort((a, b) => string.CompareOrdinal(a.Emitter.TargetName, b.Emitter.TargetName));

        // CFG004 — output path must be relative and resolve inside the output root.
        // Runs BEFORE HOST003 so a rooted/escaping path is reported in its own diagnostic
        // rather than silently colliding with another rooted output.
        bool unsafePath = false;
        if (outputRoot is not null)
        {
            string canonicalOutputRoot = Path.GetFullPath(outputRoot);
            foreach (var (e, cfg) in selected)
            {
                if (Path.IsPathRooted(cfg.Output))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        RuleIds.Cfg004,
                        "emitter '" + e.TargetName + "' output path '" + cfg.Output + "' must be a relative path",
                        new SourceSpan(e.TargetName, 1, 1, 0)));
                    unsafePath = true;
                    continue;
                }
                string canonicalCandidate = Path.GetFullPath(Path.Combine(canonicalOutputRoot, cfg.Output));
                if (!IsWithinRoot(canonicalCandidate, canonicalOutputRoot))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        RuleIds.Cfg004,
                        "emitter '" + e.TargetName + "' output '" + cfg.Output
                            + "' resolves outside the output root '" + canonicalOutputRoot + "'",
                        new SourceSpan(e.TargetName, 1, 1, 0)));
                    unsafePath = true;
                }
            }
            if (unsafePath)
            {
                var empty = ImmutableSortedDictionary.Create<string, BufferedEmitterOutput>(StringComparer.Ordinal);
                return new RunResult(SortDiagnostics(diagnostics), empty);
            }
        }

        // HOST003 — overlapping output directories.
        var seenOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
        bool overlap = false;
        foreach (var (e, cfg) in selected)
        {
            var canonical = CanonicalizeOutput(cfg.Output);
            if (seenOutputs.TryGetValue(canonical, out var firstTarget))
            {
                // Emit one diagnostic per colliding pair (sorted by ordinal name).
                var names = new[] { firstTarget, e.TargetName };
                Array.Sort(names, StringComparer.Ordinal);
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Host003,
                    "emitters '" + names[0] + "' and '" + names[1] + "' are both configured with output '"
                        + cfg.Output + "'; output directories must be disjoint",
                    new SourceSpan(canonical, 1, 1, 0)));
                overlap = true;
            }
            else
            {
                seenOutputs[canonical] = e.TargetName;
            }
        }

        if (overlap)
        {
            // Abort before invoking any emitter, per plan.md §3.6 pre-flight contract.
            var empty = ImmutableSortedDictionary.Create<string, BufferedEmitterOutput>(StringComparer.Ordinal);
            return new RunResult(SortDiagnostics(diagnostics), empty);
        }

        var buffers = new ConcurrentDictionary<string, BufferedEmitterOutput>(StringComparer.Ordinal);
        var emitterDiags = new ConcurrentBag<Diagnostic>();

        await Parallel.ForEachAsync(selected, cancellationToken, (entry, ct) =>
        {
            var (e, cfg) = entry;
            var sink = new BufferedEmitterOutput();
            EmitResult result;
            try
            {
                result = e.Emit(model, cfg, sink);
            }
            catch (Exception ex)
            {
                emitterDiags.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "HOST004",
                    "emitter '" + e.TargetName + "' threw " + ex.GetType().Name + ": " + ex.Message,
                    new SourceSpan(e.TargetName, 1, 1, 0)));
                buffers[e.TargetName] = sink;
                return ValueTask.CompletedTask;
            }
            foreach (var d in result.Diagnostics)
            {
                emitterDiags.Add(d);
            }
            buffers[e.TargetName] = sink;
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        diagnostics.AddRange(emitterDiags);

        // Commit buffers in ordinal target order.
        if (outputRoot is not null)
        {
            Directory.CreateDirectory(outputRoot);
            foreach (var (e, cfg) in selected)
            {
                if (!buffers.TryGetValue(e.TargetName, out var buf)) continue;
                var emitterRoot = Path.IsPathRooted(cfg.Output)
                    ? cfg.Output
                    : Path.Combine(outputRoot, cfg.Output);
                buf.CommitTo(emitterRoot);
            }
        }

        var bufferMap = ImmutableSortedDictionary.CreateBuilder<string, BufferedEmitterOutput>(StringComparer.Ordinal);
        foreach (var kv in buffers)
        {
            bufferMap[kv.Key] = kv.Value;
        }

        return new RunResult(SortDiagnostics(diagnostics), bufferMap.ToImmutable());
    }

    private static ImmutableArray<Diagnostic> SortDiagnostics(ImmutableArray<Diagnostic>.Builder builder)
    {
        var arr = builder.ToArray();
        Array.Sort(arr, (a, b) =>
        {
            int c = string.CompareOrdinal(a.Span.Path, b.Span.Path);
            if (c != 0) return c;
            c = a.Span.Line.CompareTo(b.Span.Line);
            if (c != 0) return c;
            c = a.Span.Column.CompareTo(b.Span.Column);
            if (c != 0) return c;
            return string.CompareOrdinal(a.RuleId, b.RuleId);
        });
        return arr.ToImmutableArray();
    }

    private static string CanonicalizeOutput(string output)
    {
        // Force forward slashes and trim trailing slashes so "gen/csharp",
        // "gen/csharp/", and "gen\\csharp" all collide deterministically.
        var s = output.Replace('\\', '/');
        return s.TrimEnd('/');
    }

    private static bool IsWithinRoot(string canonicalCandidate, string canonicalRoot)
    {
        if (string.Equals(canonicalCandidate, canonicalRoot, StringComparison.Ordinal)) return true;
        string prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalCandidate.StartsWith(prefix, StringComparison.Ordinal);
    }
}
