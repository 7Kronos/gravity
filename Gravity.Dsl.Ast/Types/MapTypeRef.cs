namespace Gravity.Dsl.Ast;

/// <summary>
/// A reference to a map (dictionary) type — an unordered collection of key/value
/// pairs, written <c>Map&lt;Key, Value&gt;</c> in source. A map models domain
/// concepts such as external identities (e.g. <c>{ "external_tool1": "KEY1" }</c>).
/// </summary>
/// <param name="Key">
/// The key type. The validator constrains this to a non-optional, non-array scalar
/// primitive (rule <c>VAL031</c>): map keys must be string-serializable so the JSON
/// Schema target can render them as object property names.
/// </param>
/// <param name="Value">The value type. May be any <see cref="TypeRef"/>.</param>
/// <remarks>
/// Per FR-011 the same <c>?</c> and <c>[]</c> modifiers that apply to other type
/// references apply to a map as a whole: <c>Map&lt;String, String&gt;?</c> is an
/// optional map. Added as a new <see cref="TypeRef"/> subtype, which is an
/// additive AST change (a new record never breaks the 1.x AST contract).
/// </remarks>
public sealed record MapTypeRef(
    TypeRef Key,
    TypeRef Value,
    bool IsOptional,
    bool IsArray,
    SourceSpan Span)
    : TypeRef(Span);
