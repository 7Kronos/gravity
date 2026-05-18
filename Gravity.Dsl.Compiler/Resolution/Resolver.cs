using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Resolution;

/// <summary>
/// Resolves a set of parsed <see cref="SourceFile"/>s into a <see cref="ResolvedModel"/>:
/// merges per-file declarations into a single FQN-keyed map, validates the import
/// graph (cycles, missing files, ambiguous simple names), and reports duplicate
/// FQNs. Rules emitted: <c>RES001..RES006</c>.
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
        if (files is null) throw new ArgumentNullException(nameof(files));
        if (inputRoot is null) throw new ArgumentNullException(nameof(inputRoot));

        var diagnostics = new List<Diagnostic>();
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

        // Build FQN map; detect duplicates.
        var declMap = ImmutableSortedDictionary.CreateBuilder<string, TopLevelDecl>(StringComparer.Ordinal);
        var firstSeenSpan = new Dictionary<string, SourceSpan>(StringComparer.Ordinal);
        foreach (var f in sortedFiles)
        {
            string ns = f.Namespace?.Name ?? string.Empty;
            foreach (var decl in f.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                if (declMap.ContainsKey(fqn))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "RES004",
                        "entity '" + fqn + "' is declared more than once; Phase 0–3 disallows multiple in-scope versions",
                        decl.Span));
                    continue;
                }
                declMap[fqn] = decl;
                firstSeenSpan[fqn] = decl.Span;
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

        // Per-file import scope: simple-name -> decl. Ambiguity caught at first use (RES005)
        // but we precompute the conflicting simple names and emit RES005 once per file/name.
        foreach (var f in sortedFiles)
        {
            string fpath = NormalizePath(f.Path);
            var simpleScope = ImmutableSortedDictionary.CreateBuilder<string, TopLevelDecl>(StringComparer.Ordinal);

            // Add own declarations first (own simple names always win over imports for this file).
            string ns = f.Namespace?.Name ?? string.Empty;
            foreach (var decl in f.Declarations)
            {
                string fqn = ns.Length == 0 ? decl.Name : ns + "." + decl.Name;
                if (declMap.TryGetValue(fqn, out var canonical))
                {
                    simpleScope[decl.Name] = canonical;
                }
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
                        if (!declMap.TryGetValue(fqn, out var canonical)) continue;
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

        // RES003: missing definition. Walk relation targets, command returns, etc., and verify each
        // unqualified name is resolvable. Primitive names are allowed without declaration.
        foreach (var f in sortedFiles)
        {
            string fpath = NormalizePath(f.Path);
            var scope = fileImports[fpath];
            foreach (var decl in f.Declarations)
            {
                if (decl is EntityDecl entity)
                {
                    CheckIdentityType(entity.Identity, scope, diagnostics);
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
                        CheckTypeRef(prop.Type, scope, diagnostics);
                    }
                    foreach (var evt in entity.Events)
                    {
                        foreach (var fld in evt.Payload)
                        {
                            CheckTypeRef(fld.Type, scope, diagnostics);
                        }
                    }
                    foreach (var cmd in entity.Commands)
                    {
                        foreach (var arg in cmd.Arguments)
                        {
                            CheckTypeRef(arg.Type, scope, diagnostics);
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
                        CheckTypeRef(fld.Type, scope, diagnostics);
                    }
                }
            }
        }

        bool hasFatal = false;
        foreach (var d in diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Error &&
                (d.RuleId == "RES001" || d.RuleId == "RES002" || d.RuleId == "RES003"
                    || d.RuleId == "RES004" || d.RuleId == "RES005" || d.RuleId == "RES006"))
            {
                hasFatal = true;
                break;
            }
        }

        var model = hasFatal
            ? null
            : new ResolvedModel(declMap.ToImmutable(), sortedFiles, fileImports);
        return new ResolveResult(model, diagnostics);
    }

    private static void CheckIdentityType(IdentityDecl id, ImmutableSortedDictionary<string, TopLevelDecl> scope,
        List<Diagnostic> diagnostics)
    {
        CheckTypeRef(id.Type, scope, diagnostics);
    }

    private static void CheckTypeRef(TypeRef tr, ImmutableSortedDictionary<string, TopLevelDecl> scope,
        List<Diagnostic> diagnostics)
    {
        if (tr is NamedTypeRef named)
        {
            if (!scope.ContainsKey(named.Name))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "RES003",
                    "name '" + named.Name + "' is not defined or imported in this scope",
                    named.Span));
            }
        }
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
