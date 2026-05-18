namespace Gravity.Dsl.Ast;

/// <summary>
/// Base for a literal value inside an annotation argument list. Concrete forms cover
/// the five literal kinds allowed by FR-050: string, integer, decimal, boolean, identifier.
/// </summary>
public abstract record AnnotationValue;

/// <summary>A <c>"…"</c> string literal annotation value.</summary>
public sealed record AnnotationStringValue(string Value) : AnnotationValue;

/// <summary>A signed-integer literal annotation value.</summary>
public sealed record AnnotationIntValue(long Value) : AnnotationValue;

/// <summary>A decimal-literal annotation value.</summary>
public sealed record AnnotationDecimalValue(decimal Value) : AnnotationValue;

/// <summary>A boolean-literal annotation value (<c>true</c> / <c>false</c>).</summary>
public sealed record AnnotationBoolValue(bool Value) : AnnotationValue;

/// <summary>An identifier-token annotation value (e.g. <c>email</c>).</summary>
public sealed record AnnotationIdentValue(string Value) : AnnotationValue;
