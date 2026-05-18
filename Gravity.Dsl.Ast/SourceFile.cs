using System.Collections.Immutable;

namespace Gravity.Dsl.Ast;

/// <summary>
/// Root AST node for one <c>.gravity</c> source file. Carries the optional namespace,
/// the (possibly empty) ordered list of imports, and the ordered list of top-level
/// declarations as they appear in source.
/// </summary>
public sealed record SourceFile(
    string Path,
    NamespaceDecl? Namespace,
    ImmutableArray<ImportDecl> Imports,
    ImmutableArray<TopLevelDecl> Declarations);
