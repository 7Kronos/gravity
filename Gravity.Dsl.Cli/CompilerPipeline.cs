using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Validation;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.CSharp;

namespace Gravity.Dsl.Cli;

/// <summary>
/// Library-callable orchestration of the parse → resolve → validate → emit pipeline.
/// Split out from <see cref="Program"/> so tests can invoke the gen workflow
/// without process boundaries (AC-3 / T052).
/// </summary>
internal static class CompilerPipeline
{
    /// <summary>Outcome of a <see cref="Check"/> or <see cref="Gen"/> invocation.</summary>
    public sealed record PipelineResult(
        bool Success,
        ImmutableArray<Diagnostic> Diagnostics);

    /// <summary>Parse + resolve + validate every <c>.gravity</c> file beneath <paramref name="inputRoot"/>.</summary>
    public static async Task<PipelineResult> Check(string inputRoot, IReadOnlyList<string>? emitterFilter = null)
    {
        if (inputRoot is null) throw new ArgumentNullException(nameof(inputRoot));

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var (files, parseDiags) = ParseAll(inputRoot);
        diags.AddRange(parseDiags);
        if (HasFatalError(parseDiags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var resolved = Resolver.Resolve(files, inputRoot);
        diags.AddRange(resolved.Diagnostics);
        if (resolved.Model is null)
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        // The registry is built up-front so the claimed annotation namespaces are
        // available to the validator regardless of whether the user emitted code.
        var registry = BuildRegistry();
        diags.AddRange(registry.Diagnostics);

        var validatorDiags = Validator.Validate(resolved.Model, registry.ClaimedAnnotationNamespaces());
        diags.AddRange(validatorDiags);

        await Task.CompletedTask.ConfigureAwait(false);
        return new PipelineResult(!HasFatalError(diags), diags.ToImmutable());
    }

    /// <summary>Full gen workflow: check + load config + run emitters into <paramref name="outputRoot"/>.</summary>
    public static async Task<PipelineResult> Gen(
        string inputRoot,
        string outputRoot,
        IReadOnlyList<string>? emitterFilter = null)
    {
        if (inputRoot is null) throw new ArgumentNullException(nameof(inputRoot));
        if (outputRoot is null) throw new ArgumentNullException(nameof(outputRoot));

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var (files, parseDiags) = ParseAll(inputRoot);
        diags.AddRange(parseDiags);
        if (HasFatalError(parseDiags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var resolved = Resolver.Resolve(files, inputRoot);
        diags.AddRange(resolved.Diagnostics);
        if (resolved.Model is null)
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var registry = BuildRegistry();
        diags.AddRange(registry.Diagnostics);

        var validatorDiags = Validator.Validate(resolved.Model, registry.ClaimedAnnotationNamespaces());
        diags.AddRange(validatorDiags);
        if (HasFatalError(validatorDiags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var configPath = Path.Combine(inputRoot, ".gravity.config");
        var configs = LoadConfigs(configPath, registry, diags);
        if (HasFatalError(diags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var filtered = ApplyEmitterFilter(configs, emitterFilter);
        var run = await EmitterHost.Run(resolved.Model, filtered, registry, outputRoot).ConfigureAwait(false);
        diags.AddRange(run.Diagnostics);

        return new PipelineResult(!HasFatalError(diags), diags.ToImmutable());
    }

    private static EmitterRegistry BuildRegistry()
    {
        // Phase 3: hard-coded reference emitter set. Plugin discovery is a future
        // gravc flag and not required for this slice.
        return EmitterRegistry.FromInstances(new IEmitter[] { new CSharpEmitter() });
    }

    private static (List<SourceFile> Files, List<Diagnostic> Diags) ParseAll(string inputRoot)
    {
        var diags = new List<Diagnostic>();
        var files = new List<SourceFile>();
        if (!Directory.Exists(inputRoot))
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "CLI001",
                "input directory does not exist: " + inputRoot,
                new SourceSpan(inputRoot, 1, 1, 0)));
            return (files, diags);
        }
        var sources = Directory.GetFiles(inputRoot, "*.gravity", SearchOption.AllDirectories);
        Array.Sort(sources, StringComparer.Ordinal);
        foreach (var src in sources)
        {
            var text = File.ReadAllText(src);
            var parsed = Parser.Parse(src, text);
            diags.AddRange(parsed.Diagnostics);
            if (parsed.File is not null)
            {
                files.Add(parsed.File);
            }
        }
        return (files, diags);
    }

    private static IReadOnlyDictionary<string, EmitterConfig> LoadConfigs(
        string configPath,
        EmitterRegistry registry,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        if (!File.Exists(configPath))
        {
            // Default: every registered emitter enabled with an output directory of
            // its TargetName. Phase 3 hard-codes a single emitter, so this is a
            // single-entry dictionary.
            var defaults = ImmutableSortedDictionary.CreateBuilder<string, EmitterConfig>(StringComparer.Ordinal);
            foreach (var e in registry.Emitters)
            {
                defaults[e.TargetName] = new EmitterConfig(
                    TargetName: e.TargetName,
                    Enabled: true,
                    Output: e.TargetName,
                    Values: ImmutableSortedDictionary<string, object>.Empty.Add("output", e.TargetName));
            }
            return defaults.ToImmutable();
        }

        var loaded = ConfigLoader.LoadFile(configPath, registry);
        diags.AddRange(loaded.Diagnostics);
        return loaded.Configs;
    }

    private static IReadOnlyDictionary<string, EmitterConfig> ApplyEmitterFilter(
        IReadOnlyDictionary<string, EmitterConfig> configs,
        IReadOnlyList<string>? emitterFilter)
    {
        if (emitterFilter is null || emitterFilter.Count == 0) return configs;
        var allow = new HashSet<string>(emitterFilter, StringComparer.Ordinal);
        var filtered = ImmutableSortedDictionary.CreateBuilder<string, EmitterConfig>(StringComparer.Ordinal);
        foreach (var kv in configs)
        {
            if (allow.Contains(kv.Key)) filtered[kv.Key] = kv.Value;
        }
        return filtered.ToImmutable();
    }

    private static bool HasFatalError(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error) return true;
        }
        return false;
    }
}
