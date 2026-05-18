using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// Immutable, sorted view of every <see cref="IEmitter"/> discovered for a single run.
/// Built by <see cref="Discover"/>; not directly constructible from outside.
/// </summary>
public sealed class EmitterRegistry
{
    private readonly ImmutableArray<IEmitter> _emitters;

    internal EmitterRegistry(ImmutableArray<IEmitter> emitters, ImmutableArray<Diagnostic> diagnostics)
    {
        _emitters = emitters;
        Diagnostics = diagnostics;
    }

    /// <summary>Diagnostics produced during discovery (HOST001, HOST002).</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Discovered emitters, sorted by <c>TargetName</c> under ordinal comparison.</summary>
    public ImmutableArray<IEmitter> Emitters => _emitters;

    /// <summary>
    /// Returns the set of non-empty annotation namespaces claimed by the registered
    /// emitters. The CLI passes this to <c>Validator.Validate</c> so VAL006 fires for
    /// any annotation whose namespace nobody claimed.
    /// </summary>
    public IReadOnlySet<string> ClaimedAnnotationNamespaces()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in _emitters)
        {
            if (!string.IsNullOrEmpty(e.AnnotationNamespace))
            {
                set.Add(e.AnnotationNamespace);
            }
        }
        return set;
    }

    /// <summary>
    /// Build a registry from a fixed set of emitter instances (e.g. those supplied by
    /// the test host or by an in-proc CLI). This is the in-process equivalent of
    /// <see cref="Discover"/>; it runs the same compatibility and ownership checks.
    /// </summary>
    public static EmitterRegistry FromInstances(IEnumerable<IEmitter> emitters)
    {
        if (emitters is null) throw new ArgumentNullException(nameof(emitters));
        return Build(emitters.ToList());
    }

    /// <summary>
    /// Scan <paramref name="pluginDirectory"/> for <c>*.dll</c> files, load each into
    /// an isolated <see cref="AssemblyLoadContext"/>, instantiate every concrete
    /// public type implementing <see cref="IEmitter"/>, and run compatibility and
    /// annotation-namespace ownership checks.
    /// </summary>
    public static EmitterRegistry Discover(string pluginDirectory)
    {
        if (pluginDirectory is null) throw new ArgumentNullException(nameof(pluginDirectory));
        var loaded = new List<IEmitter>();
        if (Directory.Exists(pluginDirectory))
        {
            var dlls = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(dlls, StringComparer.Ordinal);
            foreach (var dll in dlls)
            {
                var ctx = new AssemblyLoadContext(name: dll, isCollectible: false);
                Assembly asm;
                try
                {
                    asm = ctx.LoadFromAssemblyPath(dll);
                }
                catch (Exception)
                {
                    // Skip assemblies that fail to load. We do not synthesise a
                    // diagnostic here because not every DLL in the directory is
                    // meant to be an emitter (e.g. transitive dependencies).
                    continue;
                }
                AppendEmittersFromAssembly(asm, loaded);
            }
        }
        return Build(loaded);
    }

    private static void AppendEmittersFromAssembly(Assembly asm, List<IEmitter> sink)
    {
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
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
            IEmitter instance;
            try
            {
                instance = (IEmitter)ctor.Invoke(null);
            }
            catch (Exception)
            {
                continue;
            }
            sink.Add(instance);
        }
    }

    private static EmitterRegistry Build(List<IEmitter> emitters)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var compatible = new List<IEmitter>();

        // HOST001 — AST version compatibility.
        foreach (var e in emitters)
        {
            bool ok;
            try
            {
                ok = e.SupportedAstVersions.Satisfies(AstVersion.Value);
            }
            catch (Exception)
            {
                ok = false;
            }
            if (!ok)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Host001,
                    "emitter '" + e.TargetName + "' declares SupportedAstVersions='"
                        + e.SupportedAstVersions + "' which excludes AstVersion '" + AstVersion.Value + "'",
                    new SourceSpan(e.TargetName, 1, 1, 0)));
                continue;
            }
            compatible.Add(e);
        }

        // Sort by TargetName for deterministic registry order.
        compatible.Sort((a, b) => string.CompareOrdinal(a.TargetName, b.TargetName));

        // HOST002 — annotation namespace ownership. We group by namespace then emit
        // one diagnostic per colliding pair (target names sorted ordinally).
        var byNs = new Dictionary<string, List<IEmitter>>(StringComparer.Ordinal);
        foreach (var e in compatible)
        {
            if (string.IsNullOrEmpty(e.AnnotationNamespace)) continue;
            if (!byNs.TryGetValue(e.AnnotationNamespace, out var list))
            {
                list = new List<IEmitter>();
                byNs[e.AnnotationNamespace] = list;
            }
            list.Add(e);
        }
        var orderedNs = byNs.Keys.ToList();
        orderedNs.Sort(StringComparer.Ordinal);
        foreach (var ns in orderedNs)
        {
            var list = byNs[ns];
            if (list.Count < 2) continue;
            var names = list.Select(x => x.TargetName).ToList();
            names.Sort(StringComparer.Ordinal);
            // Emit one diagnostic per unordered pair (i<j) so a 3-way collision
            // surfaces all three pairings.
            for (int i = 0; i < names.Count; i++)
            {
                for (int j = i + 1; j < names.Count; j++)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        RuleIds.Host002,
                        "annotation namespace '" + ns + "' is claimed by both '" + names[i]
                            + "' and '" + names[j] + "'",
                        new SourceSpan(ns, 1, 1, 0)));
                }
            }
        }

        return new EmitterRegistry(compatible.ToImmutableArray(), diagnostics.ToImmutable());
    }
}
