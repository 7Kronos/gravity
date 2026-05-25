using System;
using System.Collections.Immutable;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// One emitter's resolved configuration block from the Gravity emitter config
/// file (canonical <c>.gravity.yaml</c>; legacy <c>.gravity.config</c> also
/// accepted), after validation against the emitter's
/// <see cref="EmitterConfigSchema"/>. Values are keyed by configuration key
/// (ordinal) and typed as <c>string</c>, <c>long</c>, or <c>bool</c> per the
/// schema's <see cref="ConfigValueKind"/>.
/// </summary>
public sealed record EmitterConfig(
    string TargetName,
    bool Enabled,
    string Output,
    ImmutableSortedDictionary<string, object> Values)
{
    /// <summary>Fetch a typed value, throwing if absent or of the wrong kind.</summary>
    public string GetString(string key)
    {
        if (!Values.TryGetValue(key, out var raw))
        {
            throw new InvalidOperationException("configuration key '" + key + "' is not set");
        }
        if (raw is string s) return s;
        throw new InvalidOperationException("configuration key '" + key + "' is not a string");
    }

    /// <summary>Fetch a typed value, throwing if absent or of the wrong kind.</summary>
    public long GetInt(string key)
    {
        if (!Values.TryGetValue(key, out var raw))
        {
            throw new InvalidOperationException("configuration key '" + key + "' is not set");
        }
        if (raw is long n) return n;
        throw new InvalidOperationException("configuration key '" + key + "' is not an integer");
    }

    /// <summary>Fetch a typed value, throwing if absent or of the wrong kind.</summary>
    public bool GetBool(string key)
    {
        if (!Values.TryGetValue(key, out var raw))
        {
            throw new InvalidOperationException("configuration key '" + key + "' is not set");
        }
        if (raw is bool b) return b;
        throw new InvalidOperationException("configuration key '" + key + "' is not a boolean");
    }
}
