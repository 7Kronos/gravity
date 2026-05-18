using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Compiler.Versioning;
using Xunit;

namespace Gravity.Dsl.Tests.Versioning;

/// <summary>
/// Unit tests for <see cref="DeclKey"/> and its comparer. Covers value equality,
/// comparison ordering (FQN ordinal first, then Version ascending), and
/// <see cref="ImmutableSortedDictionary{TKey,TValue}"/> iteration order with
/// <see cref="DeclKeyComparer.Instance"/>. Pins the FR-161 contract that the
/// multi-version declaration map iterates in <c>(Fqn ordinal asc, Version asc)</c>.
/// </summary>
public sealed class DeclKeyTests
{
    [Fact]
    public void Equality_SameFqnAndVersion_AreEqual()
    {
        var a = new DeclKey("ops.Employee", 1);
        var b = new DeclKey("ops.Employee", 1);
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentVersion_AreNotEqual()
    {
        var a = new DeclKey("ops.Employee", 1);
        var b = new DeclKey("ops.Employee", 2);
        a.Should().NotBe(b);
    }

    [Fact]
    public void CompareTo_OrdersByFqnOrdinalThenVersionAscending()
    {
        // FQN ordinal: "A" < "B" < "a" (ordinal puts uppercase before lowercase).
        var a1 = new DeclKey("A", 1);
        var a2 = new DeclKey("A", 2);
        var b1 = new DeclKey("B", 1);
        var lower = new DeclKey("a", 1);

        a1.CompareTo(a2).Should().BeLessThan(0);
        a2.CompareTo(b1).Should().BeLessThan(0);
        b1.CompareTo(lower).Should().BeLessThan(0);
        a1.CompareTo(a1).Should().Be(0);
        a2.CompareTo(a1).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ImmutableSortedDictionary_WithDeclKeyComparer_IteratesInExpectedOrder()
    {
        // Insertion order intentionally scrambled; expected iteration is
        // ("A", 1), ("A", 2), ("B", 1), ("B", 3).
        var builder = ImmutableSortedDictionary.CreateBuilder<DeclKey, string>(DeclKeyComparer.Instance);
        builder[new DeclKey("B", 3)] = "b3";
        builder[new DeclKey("A", 2)] = "a2";
        builder[new DeclKey("B", 1)] = "b1";
        builder[new DeclKey("A", 1)] = "a1";

        var actual = builder.Select(kv => (kv.Key.Fqn, kv.Key.Version)).ToList();
        actual.Should().Equal(
            new List<(string, int)>
            {
                ("A", 1),
                ("A", 2),
                ("B", 1),
                ("B", 3),
            });
    }
}
