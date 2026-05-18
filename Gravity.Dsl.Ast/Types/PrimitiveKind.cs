namespace Gravity.Dsl.Ast;

/// <summary>
/// Built-in primitive kinds available in Phase 0–3 (FR-010). No others.
/// Order is the canonical iteration order for emitters that fan out per primitive.
/// </summary>
public enum PrimitiveKind
{
    String,
    Int,
    Long,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Uuid
}
