using System;
using System.Collections.Generic;

namespace Gravity.Dsl.Compiler.Versioning;

/// <summary>
/// Composite key for the multi-version declaration map (Phase 8, FR-120).
/// <para>
/// Pairs an FQN with its per-declaration version. Ordering is <c>(Fqn ordinal asc, Version asc)</c>
/// — the FR-161 contract — so an <see cref="System.Collections.Immutable.ImmutableSortedDictionary{TKey,TValue}"/>
/// keyed by <see cref="DeclKey"/> iterates deterministically across multi-version coexistence.
/// </para>
/// </summary>
public readonly record struct DeclKey(string Fqn, int Version) : IComparable<DeclKey>
{
    public int CompareTo(DeclKey other)
    {
        int c = string.CompareOrdinal(Fqn, other.Fqn);
        return c != 0 ? c : Version.CompareTo(other.Version);
    }
}

/// <summary>
/// Singleton <see cref="IComparer{T}"/> for <see cref="DeclKey"/>; pass <see cref="Instance"/>
/// to <c>ImmutableSortedDictionary.CreateBuilder&lt;DeclKey, T&gt;(...)</c> to lock in the
/// FR-161 iteration order. Promoted to <c>public</c> alongside <see cref="DeclKey"/> so
/// downstream emitters that need the FR-161 ordering contract can construct sorted
/// dictionaries keyed by <see cref="DeclKey"/> without copying the comparer.
/// </summary>
public sealed class DeclKeyComparer : IComparer<DeclKey>
{
    public static readonly DeclKeyComparer Instance = new();

    public int Compare(DeclKey x, DeclKey y) => x.CompareTo(y);
}
