namespace Gravity.Dsl.Ast;

/// <summary>
/// A reference to a user-declared type (a <see cref="ValueTypeDecl"/> or
/// <see cref="EnumDecl"/>) by simple name. The resolver attaches the fully
/// qualified name later; the AST stores the as-written identifier.
/// </summary>
/// <param name="Version">
/// Optional version qualifier from a <c>Foo@N</c> source suffix (Phase 8, FR-100/FR-110).
/// <c>null</c> when the source had no <c>@N</c> suffix; the resolver then binds the ref to the
/// maximum declared version of the FQN in scope (FR-126). Added as the LAST positional
/// parameter with a default of <c>null</c> to preserve the 1.0.0 4-argument constructor
/// signature: <c>new NamedTypeRef(name, isOpt, isArr, span)</c> continues to compile and
/// run identically against 1.1.0 (FR-111).
/// </param>
public sealed record NamedTypeRef(
    string Name,
    bool IsOptional,
    bool IsArray,
    SourceSpan Span,
    int? Version = null)
    : TypeRef(Span);
