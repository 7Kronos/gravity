using System.Text;
using System.Text.RegularExpressions;

namespace Gravity.Dsl.Emitter.PostgresDdl.Render;

/// <summary>
/// Pure deterministic helpers for PostgreSQL identifier mapping (FR-451) and
/// validation (FR-470). No I/O, no clock, no machine identity — banned-APIs
/// analyzer will catch any regression.
/// </summary>
internal static class Identifier
{
    private static readonly Regex PgIdent =
        new("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const int PgMaxIdentLength = 63; // NAMEDATALEN - 1 in default Postgres builds.

    /// <summary>
    /// Returns true when <paramref name="s"/> matches <c>[a-z_][a-z0-9_]*</c>,
    /// length 1..63 (PostgreSQL's NAMEDATALEN−1 default). Used to validate the
    /// <c>schema</c> config value (PG001) and the
    /// <c>@postgres(column: "&lt;value&gt;")</c> override (PG004).
    /// </summary>
    public static bool IsValidPgIdentifier(string s)
        => !string.IsNullOrEmpty(s) && s.Length <= PgMaxIdentLength && PgIdent.IsMatch(s);

    /// <summary>
    /// Defense-in-depth: escape a string for safe inclusion as a single-quoted
    /// PostgreSQL string literal. Doubles internal single quotes per the SQL
    /// standard. The DSL grammar already prevents single quotes inside enum
    /// variants and lifecycle states (identifier rule is <c>[A-Za-z][A-Za-z0-9_]*</c>),
    /// but synthetic <c>ResolvedModel</c> inputs that
    /// bypass the parser are part of the public AST contract and could carry
    /// arbitrary bytes — local escaping keeps the emitter's contract
    /// self-enforcing rather than transitively dependent on the parser.
    /// </summary>
    public static string QuotePgString(string s)
        => "'" + s.Replace("'", "''") + "'";

    /// <summary>
    /// Acronym-aware snake_case mapping per FR-451. Worked examples:
    /// <c>firstName → first_name</c>, <c>FirstName → first_name</c>,
    /// <c>HTTPSResponse → https_response</c>, <c>URL → url</c>,
    /// <c>URLPath → url_path</c>, <c>first_name → first_name</c> (idempotent),
    /// <c>Employee → employee</c>.
    /// </summary>
    public static string ToSnakeCase(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return identifier;
        var sb = new StringBuilder(identifier.Length + 4);
        for (int i = 0; i < identifier.Length; i++)
        {
            char c = identifier[i];
            if (char.IsUpper(c))
            {
                bool prevIsLowerOrDigit = i > 0 && (char.IsLower(identifier[i - 1]) || char.IsDigit(identifier[i - 1]));
                bool nextIsLower = i + 1 < identifier.Length && char.IsLower(identifier[i + 1]);
                bool prevIsUpper = i > 0 && char.IsUpper(identifier[i - 1]);
                if (sb.Length > 0 && (prevIsLowerOrDigit || (prevIsUpper && nextIsLower)))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
