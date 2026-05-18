using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// One <c>@namespace(key: value, ...)</c> annotation. Argument keys are stored in
/// an <see cref="ImmutableSortedDictionary{TKey, TValue}"/> (<see cref="StringComparer.Ordinal"/>)
/// so iteration order is deterministic across runs and platforms (Principle I,
/// plan.md §4 determinism strategy). <see cref="ImmutableDictionary{TKey, TValue}"/>
/// is deliberately not used because its enumeration order is hash-bucket order,
/// which is stable per process but not across architectures.
/// </summary>
public sealed record AnnotationDecl(
    string Namespace,
    string Name,
    ImmutableSortedDictionary<string, AnnotationValue> Arguments,
    SourceSpan Span);
