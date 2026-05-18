using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Compiler.Resolution;

/// <summary>
/// Resolves a set of parsed <see cref="SourceFile"/>s into a <see cref="ResolvedModel"/>:
/// merges per-file declarations into a single <see cref="DeclKey"/>-keyed map, validates
/// the import graph (cycles, missing files, ambiguous simple names), and admits
/// multi-version coexistence only through an explicit <c>deprecates</c> chain
/// (Phase 8, FR-120..FR-127). Rules emitted: <c>RES001..RES006</c>.
/// </summary>
public static class Resolver
{
    /// <summary>
    /// Resolve every file in <paramref name="files"/> against the canonicalized
    /// <paramref name="inputRoot"/>. Imports must be relative paths that, after
    /// canonicalization, resolve at or beneath <paramref name="inputRoot"/>;
    /// anything else is rejected with <c>RES006</c>.
    /// </summary>
    public static ResolveResult Resolve(IReadOnlyList<SourceFile> files, string inputRoot)
    {
        var (result, _) = ResolveCore(files, inputRoot, collectBindings: false);
        return result;
    }

    /// <summary>
    /// Test-only overload that exposes the resolver's internal type-ref-to-decl binding
    /// table. Returns the standard <see cref="ResolveResult"/> plus a dictionary mapping
    /// each resolved <see cref="NamedTypeRef"/> to its bound <see cref="DeclKey"/> so
    /// callers can assert (e.g. AC-8.13) that an unqualified <c>Project</c> bound to
    /// <c>Project@2</c>. The dictionary contains only successful bindings; unresolved
    /// type refs are omitted. See plan.md §3.4(e).
    /// </summary>
    internal static (ResolveResult Result, IReadOnlyDictionary<NamedTypeRef, DeclKey> Bindings)
        ResolveWithBindings(IReadOnlyList<SourceFile> files, string inputRoot)
        => ResolveCore(files, inputRoot, collectBindings: true);

    private static (ResolveResult Result, IReadOnlyDictionary<NamedTypeRef, DeclKey> Bindings)
        ResolveCore(IReadOnlyList<SourceFile> files, string inputRoot, bool collectBindings)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));
        if (inputRoot is null) throw new ArgumentNullException(nameof(inputRoot));

        var diagnostics = new List<Diagnostic>();
        var bindings = collectBindings
            ? new Dictionary<NamedTypeRef, DeclKey>(ReferenceEqualityComparer.Instance)
            : null;
        string canonicalInputRoot = Path.GetFullPath(inputRoot);

        // Sort input files by path for determinism (plan.md §4).
        var sortedFiles = new List<SourceFile>(files);
        sortedFiles.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        // Index files by absolute and normalized path so import resolution is deterministic.
        var byPath = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var f in sortedFiles)
        {
            byPath[NormalizePath(f.Path)] = f;
        }

        // FQN → list of (DeclKey, decl) pairs, collected before chain admission so we
        // can apply the multi-version rules of FR-121..FR-123 in a single pass.
        var declMap = ImmutableSortedDictionary.CreateBuilder<DeclKey, TopLevelDecl>(DeclKeyComparer.Instance);
        var sameVersionDuplicate = new HashSet<DeclKey>();
        foreach (var f in sortedFiles)
        {
            string ns = f.Namespace?.Name ?? string.Empty;
            foreach (var decl in f.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                var key = new DeclKey(fqn, decl.Version);
                if (declMap.ContainsKey(key))
                {
                    // FR-121: same (FQN, Version) — unchanged Phase 0–3 duplicate-version error.
                    if (sameVersionDuplicate.Add(key))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            "RES004",
                            "entity '" + fqn + "' is declared more than once; multi-version coexistence requires a deprecates chain",
                            decl.Span));
                    }
                    continue;
                }
                declMap[key] = decl;
            }
        }

        // FR-122 / FR-123: walk FQN groups with multiple versions and require a deprecates
        // chain between every adjacent pair. Build the version index from the same data so
        // it is consistent with what survives in declMap.
        var versionIndex = BuildVersionIndex(declMap);
        foreach (var kv in versionIndex)
        {
            var fqn = kv.Key;
            var versions = kv.Value;
            if (versions.Length <= 1) continue;
            for (int i = 1; i < versions.Length; i++)
            {
                int prev = versions[i - 1];
                int next = versions[i];
                var nextDecl = declMap[new DeclKey(fqn, next)];
                bool chained = nextDecl is EntityDecl ent
                    && ent.Deprecates is { } dep
                    && dep.Version == prev;
                if (!chained)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "RES004",
                        "entity '" + fqn + "' is declared more than once; multi-version coexistence requires a deprecates chain",
                        nextDecl.Span));
                }
            }
        }

        // Validate import graph: missing imports, cycles.
        var fileImports = new Dictionary<string, ImmutableSortedDictionary<string, TopLevelDecl>>(StringComparer.Ordinal);

        // Build adjacency for cycle detection (file -> imported files).
        var importsResolved = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var f in sortedFiles)
        {
            var resolvedPaths = new List<string>();
            foreach (var imp in f.Imports)
            {
                // RES006a: rooted import paths are rejected outright; the resolver only
                // accepts relative paths that resolve inside the input root.
                if (Path.IsPathRooted(imp.RelativePath))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "RES006",
                        "import '" + imp.RelativePath + "' must be a relative path within the input root",
                        imp.Span));
                    continue;
                }
                string resolved = NormalizePath(
                    Path.Combine(Path.GetDirectoryName(f.Path) ?? string.Empty, imp.RelativePath));
                // RES006b: canonicalized import must remain at or under the input root.
                if (!IsWithinRoot(resolved, canonicalInputRoot))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "RES006",
                        "import '" + imp.RelativePath + "' resolves outside the input root '" + canonicalInputRoot + "'",
                        imp.Span));
                    continue;
                }
                if (!byPath.ContainsKey(resolved))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "RES002",
                        "import '" + imp.RelativePath + "' could not be resolved (file not found at " + resolved + ")",
                        imp.Span));
                    continue;
                }
                resolvedPaths.Add(resolved);
            }
            importsResolved[NormalizePath(f.Path)] = resolvedPaths;
        }

        // Cycle detection via DFS.
        var cycleReported = new HashSet<string>(StringComparer.Ordinal);
        var color = new Dictionary<string, int>(StringComparer.Ordinal); // 0=white, 1=gray, 2=black
        foreach (var f in sortedFiles)
        {
            DetectCycles(NormalizePath(f.Path), importsResolved, color, new Stack<string>(),
                cycleReported, diagnostics, byPath);
        }

        // Per-file imports-transitive closure: file paths reachable from each file
        // (own file included) via the resolved import graph. Used by FR-126 to filter
        // VersionIndex to versions "in scope" for unqualified refs.
        var transitiveImports = BuildTransitiveImports(sortedFiles, importsResolved);

        // Per-file import scope: simple-name -> canonical decl (max-version preferred).
        // Ambiguity caught at first use (RES005) but we precompute the conflicting simple
        // names and emit RES005 once per file/name.
        foreach (var f in sortedFiles)
        {
            string fpath = NormalizePath(f.Path);
            var simpleScope = ImmutableSortedDictionary.CreateBuilder<string, TopLevelDecl>(StringComparer.Ordinal);

            // Helper: pick the max-version decl for a given simple name out of (ns + name).
            void Bind(string simpleName, string fqn)
            {
                if (!versionIndex.TryGetValue(fqn, out var vs) || vs.Length == 0) return;
                int maxVersion = vs[vs.Length - 1];
                if (declMap.TryGetValue(new DeclKey(fqn, maxVersion), out var canonical))
                {
                    simpleScope[simpleName] = canonical;
                }
            }

            // Add own declarations first (own simple names always win over imports for this file).
            string ns = f.Namespace?.Name ?? string.Empty;
            foreach (var decl in f.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                Bind(decl.Name, fqn);
            }

            // Track which imported file each simple name came from for ambiguity reporting.
            var importedFrom = new Dictionary<string, string>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);

            if (importsResolved.TryGetValue(fpath, out var imps))
            {
                foreach (var impPath in imps)
                {
                    if (!byPath.TryGetValue(impPath, out var imported)) continue;
                    string impNs = imported.Namespace?.Name ?? string.Empty;
                    foreach (var decl in imported.Declarations)
                    {
                        string fqn = impNs.Length == 0 ? decl.Name : impNs + "." + decl.Name;
                        if (!versionIndex.TryGetValue(fqn, out var vs) || vs.Length == 0) continue;
                        int maxVersion = vs[vs.Length - 1];
                        if (!declMap.TryGetValue(new DeclKey(fqn, maxVersion), out var canonical)) continue;
                        // Own decls override imports without conflict; only imports vs imports conflict.
                        if (simpleScope.TryGetValue(decl.Name, out var existing))
                        {
                            // If the existing entry is the same canonical declaration (e.g. same file imported via
                            // different paths) skip; otherwise mark ambiguous.
                            if (!ReferenceEquals(existing, canonical))
                            {
                                if (importedFrom.ContainsKey(decl.Name))
                                {
                                    // Both came from imports — ambiguous.
                                    if (!ambiguous.Contains(decl.Name))
                                    {
                                        ambiguous.Add(decl.Name);
                                        diagnostics.Add(new Diagnostic(
                                            DiagnosticSeverity.Error,
                                            "RES005",
                                            "name '" + decl.Name + "' is ambiguous; imported from "
                                                + importedFrom[decl.Name] + " and " + impPath,
                                            f.Imports.Length > 0 ? f.Imports[0].Span : decl.Span));
                                    }
                                }
                            }
                            continue;
                        }
                        simpleScope[decl.Name] = canonical;
                        importedFrom[decl.Name] = impPath;
                    }
                }
            }

            fileImports[fpath] = simpleScope.ToImmutable();
        }

        // Build a per-file map: simple-name -> FQN, for version-aware lookups in CheckTypeRef.
        var fileSimpleToFqn = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        // Build a per-(FQN, Version) -> declaring file path. Used to filter VersionIndex
        // by the importing file's imports-transitive scope per FR-126.
        var declKeyToFile = new Dictionary<DeclKey, string>();
        foreach (var f in sortedFiles)
        {
            string ns = f.Namespace?.Name ?? string.Empty;
            string fpath = NormalizePath(f.Path);
            foreach (var decl in f.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                var key = new DeclKey(fqn, decl.Version);
                if (!declKeyToFile.ContainsKey(key))
                {
                    declKeyToFile[key] = fpath;
                }
            }
        }
        foreach (var f in sortedFiles)
        {
            string fpath = NormalizePath(f.Path);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string ns = f.Namespace?.Name ?? string.Empty;
            // Own decls
            foreach (var decl in f.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                map[decl.Name] = fqn;
            }
            // Imports
            if (importsResolved.TryGetValue(fpath, out var imps))
            {
                foreach (var impPath in imps)
                {
                    if (!byPath.TryGetValue(impPath, out var imported)) continue;
                    string impNs = imported.Namespace?.Name ?? string.Empty;
                    foreach (var decl in imported.Declarations)
                    {
                        string fqn = impNs.Length == 0 ? decl.Name : impNs + "." + decl.Name;
                        if (!map.ContainsKey(decl.Name))
                        {
                            map[decl.Name] = fqn;
                        }
                    }
                }
            }
            fileSimpleToFqn[fpath] = map;
        }

        // RES003: missing definition. Walk relation targets, command returns, properties,
        // event payloads, and command arguments and verify each named type ref is resolvable.
        // Phase 8 (FR-126/FR-127) extends the unqualified case to filter by imports-transitive
        // scope and the qualified case to emit a missing-version variant of RES003.
        foreach (var f in sortedFiles)
        {
            string fpath = NormalizePath(f.Path);
            var scope = fileImports[fpath];
            var simpleToFqn = fileSimpleToFqn[fpath];
            var visible = transitiveImports[fpath];
            foreach (var decl in f.Declarations)
            {
                if (decl is EntityDecl entity)
                {
                    CheckIdentityType(entity.Identity, scope, simpleToFqn, visible, declKeyToFile,
                        versionIndex, declMap, bindings, diagnostics);
                    foreach (var rel in entity.Relations)
                    {
                        if (!scope.ContainsKey(rel.TargetEntity))
                        {
                            diagnostics.Add(new Diagnostic(
                                DiagnosticSeverity.Error,
                                "RES003",
                                "name '" + rel.TargetEntity + "' is not defined or imported in this scope",
                                rel.Span));
                        }
                    }
                    foreach (var prop in entity.Properties)
                    {
                        CheckTypeRef(prop.Type, scope, simpleToFqn, visible, declKeyToFile,
                            versionIndex, declMap, bindings, diagnostics);
                    }
                    foreach (var evt in entity.Events)
                    {
                        foreach (var fld in evt.Payload)
                        {
                            CheckTypeRef(fld.Type, scope, simpleToFqn, visible, declKeyToFile,
                                versionIndex, declMap, bindings, diagnostics);
                        }
                    }
                    foreach (var cmd in entity.Commands)
                    {
                        foreach (var arg in cmd.Arguments)
                        {
                            CheckTypeRef(arg.Type, scope, simpleToFqn, visible, declKeyToFile,
                                versionIndex, declMap, bindings, diagnostics);
                        }
                        if (!scope.ContainsKey(cmd.ReturnsType))
                        {
                            diagnostics.Add(new Diagnostic(
                                DiagnosticSeverity.Error,
                                "RES003",
                                "name '" + cmd.ReturnsType + "' is not defined or imported in this scope",
                                cmd.Span));
                        }
                    }
                }
                else if (decl is ValueTypeDecl vt)
                {
                    foreach (var fld in vt.Fields)
                    {
                        CheckTypeRef(fld.Type, scope, simpleToFqn, visible, declKeyToFile,
                            versionIndex, declMap, bindings, diagnostics);
                    }
                }
            }
        }

        // Phase 8 (P8c): RES004 (multi-version coexistence without a deprecates chain)
        // no longer voids the model so the validator's breaking-change pass can still
        // run (and the per-decl rules VAL027/VAL028/VAL029/VAL030 can fire on the
        // surviving declarations). The pipeline still reports failure because the
        // RES004 diagnostic remains an Error; codegen will not run while any Error
        // is present, so user-facing semantics are unchanged.
        bool hasFatal = false;
        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error &&
                (d.RuleId == "RES001" || d.RuleId == "RES002" || d.RuleId == "RES003"
                    || d.RuleId == "RES005" || d.RuleId == "RES006"))
            {
                hasFatal = true;
                break;
            }
        }

        var model = hasFatal
            ? null
            : new ResolvedModel(declMap.ToImmutable(), sortedFiles, fileImports)
            {
                VersionIndex = versionIndex,
            };
        return (
            new ResolveResult(model, diagnostics),
            bindings ?? (IReadOnlyDictionary<NamedTypeRef, DeclKey>)
                new Dictionary<NamedTypeRef, DeclKey>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Build the FQN → versions-ascending index (FR-122). Phase 8 multi-version
    /// admission and FR-126's "resolve unqualified to max" both consume this.
    /// </summary>
    private static ImmutableSortedDictionary<string, ImmutableArray<int>> BuildVersionIndex(
        ImmutableSortedDictionary<DeclKey, TopLevelDecl>.Builder declMap)
    {
        var grouped = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var kv in declMap)
        {
            if (!grouped.TryGetValue(kv.Key.Fqn, out var list))
            {
                list = new List<int>();
                grouped[kv.Key.Fqn] = list;
            }
            list.Add(kv.Key.Version);
        }
        var builder = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<int>>(StringComparer.Ordinal);
        foreach (var kv in grouped)
        {
            kv.Value.Sort();
            builder[kv.Key] = kv.Value.ToImmutableArray();
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Build per-file transitive import closures (file's own path plus every file reachable
    /// via the resolved import graph). Used by FR-126 to filter the version index to
    /// versions declared in files in scope.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildTransitiveImports(
        IReadOnlyList<SourceFile> sortedFiles,
        Dictionary<string, List<string>> importsResolved)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var f in sortedFiles)
        {
            string fpath = NormalizePath(f.Path);
            var visited = new HashSet<string>(StringComparer.Ordinal) { fpath };
            var stack = new Stack<string>();
            stack.Push(fpath);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!importsResolved.TryGetValue(node, out var children)) continue;
                foreach (var c in children)
                {
                    if (visited.Add(c))
                    {
                        stack.Push(c);
                    }
                }
            }
            result[fpath] = visited;
        }
        return result;
    }

    private static void CheckIdentityType(
        IdentityDecl id,
        ImmutableSortedDictionary<string, TopLevelDecl> scope,
        Dictionary<string, string> simpleToFqn,
        HashSet<string> visibleFiles,
        Dictionary<DeclKey, string> declKeyToFile,
        ImmutableSortedDictionary<string, ImmutableArray<int>> versionIndex,
        ImmutableSortedDictionary<DeclKey, TopLevelDecl>.Builder declMap,
        Dictionary<NamedTypeRef, DeclKey>? bindings,
        List<Diagnostic> diagnostics)
    {
        CheckTypeRef(id.Type, scope, simpleToFqn, visibleFiles, declKeyToFile,
            versionIndex, declMap, bindings, diagnostics);
    }

    private static void CheckTypeRef(
        TypeRef tr,
        ImmutableSortedDictionary<string, TopLevelDecl> scope,
        Dictionary<string, string> simpleToFqn,
        HashSet<string> visibleFiles,
        Dictionary<DeclKey, string> declKeyToFile,
        ImmutableSortedDictionary<string, ImmutableArray<int>> versionIndex,
        ImmutableSortedDictionary<DeclKey, TopLevelDecl>.Builder declMap,
        Dictionary<NamedTypeRef, DeclKey>? bindings,
        List<Diagnostic> diagnostics)
    {
        if (tr is not NamedTypeRef named) return;

        // Simple-name in scope? (Phase 0–3 semantics for "name not found".)
        if (!scope.ContainsKey(named.Name))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "RES003",
                "name '" + named.Name + "' is not defined or imported in this scope",
                named.Span));
            return;
        }

        // Recover the FQN bound to this simple name in this file's scope.
        if (!simpleToFqn.TryGetValue(named.Name, out var fqn))
        {
            // Defensive: if we have it in `scope` but not in our parallel map, fall back
            // to the Phase 0–3 message — should not happen, but keeps behaviour safe.
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "RES003",
                "name '" + named.Name + "' is not defined or imported in this scope",
                named.Span));
            return;
        }

        if (named.Version is { } requestedVersion)
        {
            // FR-127: exact-version lookup. Hit -> bind. Miss but name in scope -> missing-version variant.
            var requestedKey = new DeclKey(fqn, requestedVersion);
            if (declMap.TryGetValue(requestedKey, out _))
            {
                bindings?.Add(named, requestedKey);
                return;
            }
            // Render available versions in ordinal ascending order. Per FR-127.
            string list = versionIndex.TryGetValue(fqn, out var vs) ? FormatVersionList(vs) : string.Empty;
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "RES003",
                "type '" + named.Name + "@" + requestedVersion.ToString(CultureInfo.InvariantCulture)
                    + "' is not declared; '" + named.Name + "' exists with versions " + list,
                named.Span));
            return;
        }

        // FR-126: unqualified -> max-version filtered to imports-transitive scope.
        // For each declared version of the FQN, the *specific (fqn, version)*'s declaring
        // file must be in the current file's imports-transitive closure (AC-8.13b).
        if (versionIndex.TryGetValue(fqn, out var allVersions))
        {
            int maxInScope = -1;
            foreach (var v in allVersions)
            {
                if (!declMap.ContainsKey(new DeclKey(fqn, v))) continue;
                if (!declKeyToFile.TryGetValue(new DeclKey(fqn, v), out var declFile)) continue;
                if (visibleFiles.Contains(declFile))
                {
                    if (v > maxInScope) maxInScope = v;
                }
            }
            if (maxInScope >= 0)
            {
                bindings?.Add(named, new DeclKey(fqn, maxInScope));
            }
        }
    }

    private static string FormatVersionList(ImmutableArray<int> versions)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < versions.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(versions[i].ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static void DetectCycles(
        string node,
        Dictionary<string, List<string>> graph,
        Dictionary<string, int> color,
        Stack<string> stack,
        HashSet<string> cycleReported,
        List<Diagnostic> diagnostics,
        Dictionary<string, SourceFile> byPath)
    {
        if (color.TryGetValue(node, out var c) && c != 0)
        {
            return;
        }
        color[node] = 1;
        stack.Push(node);
        if (graph.TryGetValue(node, out var children))
        {
            foreach (var child in children)
            {
                if (!color.TryGetValue(child, out var cc) || cc == 0)
                {
                    DetectCycles(child, graph, color, stack, cycleReported, diagnostics, byPath);
                }
                else if (cc == 1)
                {
                    // Cycle detected; collect path.
                    var pathArr = stack.ToArray();
                    Array.Reverse(pathArr);
                    int idx = Array.IndexOf(pathArr, child);
                    var cycle = idx >= 0 ? new List<string>() : new List<string>();
                    if (idx >= 0)
                    {
                        for (int i = idx; i < pathArr.Length; i++) cycle.Add(pathArr[i]);
                        cycle.Add(child);
                    }
                    var key = string.Join("->", cycle);
                    if (!cycleReported.Contains(key))
                    {
                        cycleReported.Add(key);
                        var span = byPath.TryGetValue(node, out var sf) && sf.Imports.Length > 0
                            ? sf.Imports[0].Span
                            : new SourceSpan(node, 1, 1, 0);
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            "RES001",
                            "import cycle detected: " + string.Join(" -> ", cycle),
                            span));
                    }
                }
            }
        }
        color[node] = 2;
        stack.Pop();
    }

    private static string NormalizePath(string path)
    {
        // Use Path.GetFullPath to canonicalize; this is deterministic per cwd.
        // For cross-platform resolution we collapse separators.
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
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
