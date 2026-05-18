using System;
using System.Collections.Generic;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Validation;

namespace Gravity.Dsl.Compiler.Versioning;

/// <summary>
/// Phase 8 (P8c) version-diff engine. Walks <see cref="ResolvedModel.Declarations"/>
/// grouped by <see cref="DeclKey.Fqn"/>, runs the per-pair breaking-change rules
/// (VAL020..VAL026) for each chained <c>(Vprev, Vnext)</c> pair, and then runs
/// the per-decl passes (VAL027, VAL028, VAL029) and the deprecation-window check
/// (VAL030) across the whole model. Diagnostics are sorted per FR-160 before
/// being returned.
/// </summary>
internal static class VersionDiff
{
    /// <summary>
    /// Single public entry point. Returns the Phase 8 diff diagnostics for the
    /// supplied <paramref name="model"/>, sorted by FR-160 keys
    /// (<c>Fqn ordinal asc, Vnext asc, RuleId ordinal asc, Span.Path ordinal asc, Span.Line asc, Span.Column asc</c>).
    /// </summary>
    public static IReadOnlyList<Diagnostic> Run(ResolvedModel model, DateOnly currentDate)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        // Phase 0–3 invariant: every resolved declaration must appear in
        // VersionIndex (the resolver populates it from the same DeclKey map).
        // When the index is empty there are no declarations at all and the
        // diff has nothing to compute — short-circuit so we do not allocate
        // a sink only to flush an empty list.
        if (model.VersionIndex.IsEmpty) return Array.Empty<Diagnostic>();

        var sink = new DiagnosticSink();

        // Group declarations by FQN; for each FQN walk adjacent (prev, next) pairs.
        // We rely on Declarations iterating in (Fqn ordinal, Version asc) order
        // (FR-161) so the grouping is single-pass.
        string? currentFqn = null;
        DeclKey? prevKey = null;
        TopLevelDecl? prevDecl = null;
        foreach (var kv in model.Declarations)
        {
            var key = kv.Key;
            var decl = kv.Value;
            if (currentFqn is null || !string.Equals(currentFqn, key.Fqn, StringComparison.Ordinal))
            {
                currentFqn = key.Fqn;
                prevKey = key;
                prevDecl = decl;
                continue;
            }
            // Same FQN as the previous decl: walk the (prev, next) link.
            if (prevDecl is not null && IsChained(prevDecl, decl, prevKey!.Value.Version))
            {
                ApplyPerPairRules(prevDecl, decl, key.Fqn, sink);
            }
            prevKey = key;
            prevDecl = decl;
        }

        // Per-decl across the whole model.
        DiffRules.ApplyVal027(model, sink);
        DiffRules.ApplyVal028(model, sink);
        DiffRules.ApplyVal029(model, sink);
        DiffRules.ApplyVal030(model, currentDate, sink);

        return sink.Flush();
    }

    /// <summary>
    /// The chain admission predicate; <paramref name="next"/> must be an entity whose
    /// <c>deprecates version &lt;N&gt;</c> clause names <paramref name="prevVersion"/>.
    /// Unchained pairs were already rejected at the resolver layer by <c>RES004</c>;
    /// returning <c>false</c> here simply skips the diff so we do not pile diagnostics
    /// on top of an already-broken model.
    /// </summary>
    private static bool IsChained(TopLevelDecl prev, TopLevelDecl next, int prevVersion)
    {
        if (next is EntityDecl ent && ent.Deprecates is { } dep)
        {
            return dep.Version == prevVersion;
        }
        // ValueTypeDecl / EnumDecl never carry a deprecates clause in v1; the
        // resolver already fired RES004 for any second version of one.
        return false;
    }

    private static void ApplyPerPairRules(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        DiffRules.ApplyVal020(prev, next, fqn, sink);
        DiffRules.ApplyVal021(prev, next, fqn, sink);
        DiffRules.ApplyVal022(prev, next, fqn, sink);
        DiffRules.ApplyVal023(prev, next, fqn, sink);
        DiffRules.ApplyVal024(prev, next, fqn, sink);
        DiffRules.ApplyVal025(prev, next, fqn, sink);
        DiffRules.ApplyVal026(prev, next, fqn, sink);
    }
}

/// <summary>
/// Accumulator that buffers Phase 8 diagnostics with their FR-160 sort keys
/// and flushes the final sorted list on <see cref="Flush"/>.
/// </summary>
internal sealed class DiagnosticSink
{
    private readonly List<Entry> _entries = new();

    /// <summary>Append a diagnostic with the FR-160 secondary key set to <paramref name="vnext"/>.</summary>
    public void Add(Diagnostic diag, string fqn, int vnext)
    {
        _entries.Add(new Entry(diag, fqn, vnext, _entries.Count));
    }

    /// <summary>
    /// Return the buffered diagnostics sorted per FR-160 keys: (1) FQN ordinal asc,
    /// (2) <c>Vnext</c> asc (for per-decl rules the decl's own version is supplied),
    /// (3) RuleId ordinal asc, (4) Span.Path ordinal asc, (5) Span.Line asc,
    /// (6) Span.Column asc. Ties on every key fall back to insertion order so the
    /// result is deterministic.
    /// </summary>
    public IReadOnlyList<Diagnostic> Flush()
    {
        _entries.Sort(Compare);
        var result = new List<Diagnostic>(_entries.Count);
        foreach (var e in _entries) result.Add(e.Diagnostic);
        return result;
    }

    private static int Compare(Entry x, Entry y)
    {
        int c = string.CompareOrdinal(x.Fqn, y.Fqn);
        if (c != 0) return c;
        c = x.Vnext.CompareTo(y.Vnext);
        if (c != 0) return c;
        c = string.CompareOrdinal(x.Diagnostic.RuleId, y.Diagnostic.RuleId);
        if (c != 0) return c;
        c = string.CompareOrdinal(x.Diagnostic.Span.Path, y.Diagnostic.Span.Path);
        if (c != 0) return c;
        c = x.Diagnostic.Span.Line.CompareTo(y.Diagnostic.Span.Line);
        if (c != 0) return c;
        c = x.Diagnostic.Span.Column.CompareTo(y.Diagnostic.Span.Column);
        if (c != 0) return c;
        return x.Sequence.CompareTo(y.Sequence);
    }

    private readonly record struct Entry(Diagnostic Diagnostic, string Fqn, int Vnext, int Sequence);
}
