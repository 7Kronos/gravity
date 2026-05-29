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
public static class CompilerPipeline
{
    /// <summary>Outcome of a <see cref="Check"/> or <see cref="Gen"/> invocation.</summary>
    public sealed record PipelineResult(
        bool Success,
        ImmutableArray<Diagnostic> Diagnostics);

    /// <summary>Parse + resolve + validate every <c>.gravity</c> file beneath <paramref name="inputRoot"/>.</summary>
    /// <param name="inputRoot">Root directory containing <c>.gravity</c> sources.</param>
    /// <param name="currentDate">Date passed to <see cref="Validator.Validate"/> for
    /// Phase 8 deprecation-window evaluation (FR-140). The CLI threads
    /// <c>--as-of</c> here; tests pass a deterministic value.</param>
    /// <param name="emitterFilter">Optional emitter whitelist; ignored by <c>check</c>.</param>
    public static async Task<PipelineResult> Check(
        string inputRoot,
        DateOnly currentDate,
        IReadOnlyList<string>? emitterFilter = null)
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

        var validatorDiags = Validator.Validate(resolved.Model, registry.ClaimedAnnotationNamespaces(), currentDate);
        diags.AddRange(validatorDiags);

        await Task.CompletedTask.ConfigureAwait(false);
        return new PipelineResult(!HasFatalError(diags), diags.ToImmutable());
    }

    /// <summary>Full gen workflow: check + load config + run emitters into <paramref name="outputRoot"/>.</summary>
    /// <param name="inputRoot">Root directory containing <c>.gravity</c> sources.</param>
    /// <param name="outputRoot">Output directory; created if missing.</param>
    /// <param name="currentDate">Date passed to <see cref="Validator.Validate"/> for
    /// Phase 8 deprecation-window evaluation (FR-140).</param>
    /// <param name="emitterFilter">Optional emitter whitelist.</param>
    public static async Task<PipelineResult> Gen(
        string inputRoot,
        string outputRoot,
        DateOnly currentDate,
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

        var validatorDiags = Validator.Validate(resolved.Model, registry.ClaimedAnnotationNamespaces(), currentDate);
        diags.AddRange(validatorDiags);
        if (HasFatalError(validatorDiags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var configPath = ConfigLoader.FindInDirectory(inputRoot);
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

    /// <summary>
    /// Full gen workflow using an explicit list of <c>.gravity</c> source files (FR-234, LD-13).
    /// Used by the MSBuild task (<c>GravityDslGenTask</c>) so per-item <c>&lt;GravityDsl&gt;</c>
    /// metadata can be honoured (FR-202). The directory-scanning overload remains the CLI's
    /// default entry; this overload is additive public surface under the LD-13 stability contract.
    /// </summary>
    /// <param name="inputs">Absolute (or working-dir-relative) paths to <c>.gravity</c> source files.</param>
    /// <param name="outputRoot">Output directory; created if missing.</param>
    /// <param name="currentDate">Date passed to <see cref="Validator.Validate"/> for
    /// Phase 8 deprecation-window evaluation (FR-140).</param>
    /// <param name="configFile">Optional explicit path to a <c>.gravity.yaml</c> file.
    /// When null, defaults to a <c>.gravity.yaml</c> sibling of the first source file.</param>
    /// <param name="emitterFilter">Optional emitter whitelist.</param>
    /// <param name="extraEmitterAssemblies">Optional absolute paths to additional emitter
    /// assemblies (e.g. <c>Gravity.Dsl.Emitter.Sample.Outline.dll</c>). Each assembly is loaded
    /// and any public concrete <see cref="IEmitter"/> implementations are registered alongside
    /// the built-in C# reference emitter (FR-224). Inherits the LD-13 stability contract.</param>
    public static async Task<PipelineResult> Gen(
        IList<string> inputs,
        string outputRoot,
        DateOnly currentDate,
        string? configFile = null,
        IReadOnlyList<string>? emitterFilter = null,
        IReadOnlyList<string>? extraEmitterAssemblies = null)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (outputRoot is null) throw new ArgumentNullException(nameof(outputRoot));

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var (files, parseDiags) = ParseFiles(inputs);
        diags.AddRange(parseDiags);
        if (HasFatalError(parseDiags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        // Resolver requires a root for FQN computation. Use the longest common ancestor
        // of the input set so namespace mapping stays stable across CLI / MSBuild paths.
        var resolverRoot = ComputeResolverRoot(inputs);
        var resolved = Resolver.Resolve(files, resolverRoot);
        diags.AddRange(resolved.Diagnostics);
        if (resolved.Model is null)
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var registry = BuildRegistry(extraEmitterAssemblies);
        diags.AddRange(registry.Diagnostics);

        var validatorDiags = Validator.Validate(resolved.Model, registry.ClaimedAnnotationNamespaces(), currentDate);
        diags.AddRange(validatorDiags);
        if (HasFatalError(validatorDiags))
        {
            return new PipelineResult(false, diags.ToImmutable());
        }

        var configPath = ResolveConfigPath(configFile, resolverRoot);
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

    private static (List<SourceFile> Files, List<Diagnostic> Diags) ParseFiles(IList<string> inputs)
    {
        var diags = new List<Diagnostic>();
        var files = new List<SourceFile>();
        // Sort ordinally so iteration order matches the directory-scanning overload's behaviour.
        var sorted = inputs.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);
        foreach (var src in sorted)
        {
            if (!File.Exists(src))
            {
                diags.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "CLI001",
                    "input file does not exist: " + src,
                    new SourceSpan(src, 1, 1, 0)));
                continue;
            }
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

    private static string ComputeResolverRoot(IList<string> inputs)
    {
        if (inputs.Count == 0) return Directory.GetCurrentDirectory();
        if (inputs.Count == 1) return Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? Directory.GetCurrentDirectory();

        // Longest common ancestor of all input file directories. MSBuild may group
        // <GravityDsl> items from different directories into the same (Output, Emitter)
        // bucket; taking the first directory after a sort silently produces wrong FQNs
        // when those directories diverge.
        var firstDir = Path.GetDirectoryName(Path.GetFullPath(inputs[0])) ?? string.Empty;
        var commonParts = firstDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToList();
        for (int i = 1; i < inputs.Count; i++)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(inputs[i])) ?? string.Empty;
            var parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            int common = 0;
            while (common < commonParts.Count && common < parts.Length
                   && string.Equals(commonParts[common], parts[common], StringComparison.Ordinal))
                common++;
            commonParts = commonParts.Take(common).ToList();
            if (commonParts.Count == 0) break;
        }
        var result = string.Join(Path.DirectorySeparatorChar, commonParts);
        return string.IsNullOrEmpty(result) ? Directory.GetCurrentDirectory() : result;
    }

    private static string? ResolveConfigPath(string? explicitConfig, string resolverRoot)
    {
        if (!string.IsNullOrEmpty(explicitConfig))
        {
            return Path.GetFullPath(explicitConfig);
        }
        return ConfigLoader.FindInDirectory(resolverRoot);
    }

    private static EmitterRegistry BuildRegistry(IReadOnlyList<string>? extraAssemblies = null)
    {
        // Phase 0–3: hard-coded reference emitter set (CSharpEmitter). Phase 9 (FR-224)
        // augments this with optional caller-supplied emitter assemblies — the MSBuild
        // task threads <GravityDslEmitterAssembly> items here so packages like
        // Gravity.Dsl.Emitter.Sample.Outline can contribute their emitters at build time.
        var instances = new List<IEmitter> { new CSharpEmitter() };
        if (extraAssemblies is not null && extraAssemblies.Count > 0)
        {
            var loaded = LoadExtraEmitters(extraAssemblies);
            instances.AddRange(loaded);
        }
        return EmitterRegistry.FromInstances(instances);
    }

    /// <summary>
    /// Load every public concrete <see cref="IEmitter"/> from each assembly path in
    /// <paramref name="assemblyPaths"/>. Mirrors <see cref="EmitterRegistry.Discover"/>
    /// in spirit but operates on an explicit file list rather than a directory scan,
    /// which is the model the MSBuild task plumbs through (FR-224 / T206 wiring).
    /// </summary>
    private static List<IEmitter> LoadExtraEmitters(IReadOnlyList<string> assemblyPaths)
    {
        var result = new List<IEmitter>();
        var sorted = assemblyPaths.ToArray();
        Array.Sort(sorted, StringComparer.Ordinal);

        // Load every emitter assembly into the SAME AssemblyLoadContext that
        // hosts Gravity.Dsl.Emitter (where IEmitter lives). Under the standalone
        // CLI this is the Default ALC; under MSBuild the task is loaded into a
        // private "MSBuild plugin" ALC, and Assembly.LoadFrom would otherwise
        // route the emitter into the Default ALC — causing the type-identity
        // mismatch that silently drops every plugin emitter with a CFG001
        // "no registered target" warning at runtime.
        var hostAlc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(IEmitter).Assembly)
            ?? System.Runtime.Loader.AssemblyLoadContext.Default;

        foreach (var path in sorted)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
            System.Reflection.Assembly asm;
            try
            {
                asm = hostAlc.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            catch (Exception)
            {
                continue;
            }
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }
            Array.Sort(types, (a, b) => string.CompareOrdinal(a?.FullName, b?.FullName));
            foreach (var t in types)
            {
                if (t is null) continue;
                if (!typeof(IEmitter).IsAssignableFrom(t)) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                if (!t.IsPublic && !t.IsNestedPublic) continue;
                var ctor = t.GetConstructor(Type.EmptyTypes);
                if (ctor is null) continue;
                try
                {
                    result.Add((IEmitter)ctor.Invoke(null));
                }
                catch (Exception)
                {
                    // Skip emitters whose ctor throws — the registry will simply omit them.
                }
            }
        }
        return result;
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
        string? configPath,
        EmitterRegistry registry,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
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
