using System;
using System.Globalization;

namespace Gravity.Dsl.Cli;

/// <summary>
/// Single source of truth for resolving the <c>--as-of</c> CLI flag and the
/// equivalent <c>&lt;GravityDslAsOf&gt;</c> MSBuild property to a
/// <see cref="DateOnly"/> (FR-141 / FR-233; plan.md §3.8). The helper centralises
/// the one allowed <see cref="DateTime.UtcNow"/> read on the build side; both the
/// CLI's <c>RunCheck</c>/<c>RunGen</c> and the MSBuild task's <c>Execute</c>
/// delegate here so the two entry points cannot drift on date defaulting
/// (constitution "Build integration parity").
/// </summary>
public static class MsBuildDateResolver
{
    /// <summary>
    /// Resolve <paramref name="rawAsOf"/> to a <see cref="DateOnly"/>. When the
    /// input is null or empty the result defaults to <c>DateTime.UtcNow</c>
    /// converted to a <see cref="DateOnly"/>. Malformed input (anything not
    /// matching <c>yyyy-MM-dd</c> under invariant culture) sets
    /// <paramref name="error"/> and returns <c>false</c>.
    /// </summary>
    /// <param name="rawAsOf">The user-supplied value, or <c>null</c>/empty for "today".</param>
    /// <param name="result">The resolved <see cref="DateOnly"/> on success.</param>
    /// <param name="error">A human-readable error message on failure; <c>null</c> on success.</param>
    /// <returns><c>true</c> when <paramref name="result"/> was populated; <c>false</c> on parse failure.</returns>
    public static bool TryResolve(string? rawAsOf, out DateOnly result, out string? error)
    {
        if (!string.IsNullOrEmpty(rawAsOf))
        {
            if (!DateOnly.TryParseExact(
                    rawAsOf, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                error = "value '" + rawAsOf + "' must be YYYY-MM-DD";
                return false;
            }
            error = null;
            return true;
        }
        result = DateOnly.FromDateTime(DateTime.UtcNow);
        error = null;
        return true;
    }
}
