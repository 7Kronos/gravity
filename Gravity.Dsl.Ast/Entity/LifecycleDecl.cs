using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// An entity's <c>lifecycle { states { ... } transitions { ... } }</c> block.
/// Per FR-024, state identifiers and event identifiers live in disjoint per-entity
/// name spaces; a state and an event may share a textual name.
/// </summary>
public sealed record LifecycleDecl(
    ImmutableArray<string> States,
    ImmutableArray<TransitionDecl> Transitions,
    SourceSpan Span);
