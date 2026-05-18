namespace Gravity.Dsl.Ast;

/// <summary>
/// Cardinality of a relation. Per FR-022, the <c>[]</c> array suffix is not legal
/// on relation targets; <c>cardinality many</c> expresses multiplicity instead.
/// The combination of <c>?</c> with <c>cardinality many</c> is forbidden (VAL010).
/// </summary>
public enum Cardinality
{
    One,
    Many
}
