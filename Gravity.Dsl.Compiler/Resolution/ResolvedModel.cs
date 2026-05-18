using System.Collections.Generic;
using System.Collections.Immutable;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Resolution;

/// <summary>
/// The post-resolver view of a Gravity program: every <see cref="TopLevelDecl"/>
/// keyed by its fully-qualified name in an <see cref="ImmutableSortedDictionary{TKey,TValue}"/>
/// with <see cref="System.StringComparer.Ordinal"/> ordering. Per-file import scopes
/// are exposed via <see cref="FileImports"/>.
/// </summary>
public sealed record ResolvedModel(
    ImmutableSortedDictionary<string, TopLevelDecl> Declarations,
    IReadOnlyList<SourceFile> Files,
    IReadOnlyDictionary<string, ImmutableSortedDictionary<string, TopLevelDecl>> FileImports);

/// <summary>
/// Result of <see cref="Resolver.Resolve"/>. <see cref="Model"/> is non-null when
/// no fatal resolution error occurred (cycles, duplicate FQN, missing imports).
/// </summary>
public sealed record ResolveResult(ResolvedModel? Model, IReadOnlyList<Diagnostic> Diagnostics);
