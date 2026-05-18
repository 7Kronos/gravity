namespace Gravity.Dsl.Ast;

/// <summary>
/// Versioned contract identifier for the public AST surface in this package.
/// </summary>
/// <remarks>
/// The AST version is independent of the DSL grammar version. Breaking changes
/// to any public AST record require a major-version bump here and a documented
/// migration path for third-party emitters (per the Gravity constitution,
/// Architectural constraints — Stable AST contract).
/// </remarks>
public static class AstVersion
{
    /// <summary>Current AST contract version. Phase 0–3 ships 1.0.0.</summary>
    public const string Value = "1.0.0";
}
