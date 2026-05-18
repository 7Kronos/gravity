using System.Collections.Generic;
using System.Collections.Immutable;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Compiler.Resolution;

/// <summary>
/// The post-resolver view of a Gravity program: every <see cref="TopLevelDecl"/>
/// keyed by <see cref="DeclKey"/> (FQN + per-decl version) in an
/// <see cref="ImmutableSortedDictionary{TKey,TValue}"/> ordered by
/// <c>(Fqn ordinal asc, Version asc)</c> — the FR-161 iteration contract. Per-file
/// import scopes are exposed via <see cref="FileImports"/>.
/// </summary>
public sealed record ResolvedModel(
    ImmutableSortedDictionary<DeclKey, TopLevelDecl> Declarations,
    IReadOnlyList<SourceFile> Files,
    IReadOnlyDictionary<string, ImmutableSortedDictionary<string, TopLevelDecl>> FileImports)
{
    /// <summary>
    /// FQN → declared versions ascending. Populated by the resolver (Phase 8, FR-122),
    /// consumed by the validator's breaking-change pass (Phase 8c). Init-only so that
    /// the primary constructor's arity remains stable for downstream callers.
    /// </summary>
    public ImmutableSortedDictionary<string, ImmutableArray<int>> VersionIndex { get; init; }
        = ImmutableSortedDictionary<string, ImmutableArray<int>>.Empty.WithComparers(System.StringComparer.Ordinal);
}

/// <summary>
/// Result of <see cref="Resolver.Resolve"/>. <see cref="Model"/> is non-null when
/// no fatal resolution error occurred (cycles, duplicate FQN, missing imports).
/// </summary>
public sealed record ResolveResult(ResolvedModel? Model, IReadOnlyList<Diagnostic> Diagnostics);
