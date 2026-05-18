using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// A space-separated AND-composition of semver comparators that an emitter declares
/// it supports. Example: <c>"&gt;=1.0.0 &lt;2.0.0"</c>. Supported operators:
/// <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c>, <c>=</c> (or no prefix).
/// </summary>
/// <remarks>
/// Pre-release and build metadata identifiers are not supported in Phase 2.
/// The grammar is deliberately tiny so it can be hand-rolled without depending
/// on a third-party semver library.
/// </remarks>
public sealed class SemanticVersionRange
{
    private readonly ImmutableArray<Comparator> _comparators;

    private SemanticVersionRange(string expression, ImmutableArray<Comparator> comparators)
    {
        Expression = expression;
        _comparators = comparators;
    }

    /// <summary>The original textual expression as supplied to <see cref="Parse"/>.</summary>
    public string Expression { get; }

    /// <summary>Parse a range expression. Throws <see cref="FormatException"/> on malformed input.</summary>
    public static SemanticVersionRange Parse(string expression)
    {
        if (expression is null) throw new ArgumentNullException(nameof(expression));
        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new FormatException("semantic version range '" + expression + "' is empty");
        }
        var builder = ImmutableArray.CreateBuilder<Comparator>(parts.Length);
        foreach (var part in parts)
        {
            builder.Add(Comparator.Parse(part));
        }
        return new SemanticVersionRange(expression, builder.ToImmutable());
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="version"/> satisfies every comparator in this range.
    /// </summary>
    public bool Satisfies(string version)
    {
        var v = Version.Parse(version);
        foreach (var c in _comparators)
        {
            if (!c.Allows(v)) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => Expression;

    private enum Op { Eq, Gt, Gte, Lt, Lte }

    private readonly record struct Comparator(Op Op, Version Bound)
    {
        public static Comparator Parse(string token)
        {
            Op op;
            int prefix;
            if (token.StartsWith(">=", StringComparison.Ordinal)) { op = Op.Gte; prefix = 2; }
            else if (token.StartsWith("<=", StringComparison.Ordinal)) { op = Op.Lte; prefix = 2; }
            else if (token.StartsWith(">", StringComparison.Ordinal)) { op = Op.Gt; prefix = 1; }
            else if (token.StartsWith("<", StringComparison.Ordinal)) { op = Op.Lt; prefix = 1; }
            else if (token.StartsWith("=", StringComparison.Ordinal)) { op = Op.Eq; prefix = 1; }
            else { op = Op.Eq; prefix = 0; }
            var versionPart = token.Substring(prefix);
            var bound = Version.Parse(versionPart);
            return new Comparator(op, bound);
        }

        public bool Allows(Version v) => Op switch
        {
            Op.Eq => v.CompareTo(Bound) == 0,
            Op.Gt => v.CompareTo(Bound) > 0,
            Op.Gte => v.CompareTo(Bound) >= 0,
            Op.Lt => v.CompareTo(Bound) < 0,
            Op.Lte => v.CompareTo(Bound) <= 0,
            _ => false
        };
    }

    private readonly record struct Version(int Major, int Minor, int Patch) : IComparable<Version>
    {
        public static Version Parse(string text)
        {
            var parts = text.Split('.');
            if (parts.Length < 1 || parts.Length > 3)
            {
                throw new FormatException("semantic version '" + text + "' must have 1..3 numeric components");
            }
            int major = ParseComponent(parts[0], text);
            int minor = parts.Length > 1 ? ParseComponent(parts[1], text) : 0;
            int patch = parts.Length > 2 ? ParseComponent(parts[2], text) : 0;
            return new Version(major, minor, patch);
        }

        private static int ParseComponent(string s, string original)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
            {
                throw new FormatException("semantic version '" + original + "' contains non-numeric component '" + s + "'");
            }
            return n;
        }

        public int CompareTo(Version other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            return Patch.CompareTo(other.Patch);
        }
    }
}
