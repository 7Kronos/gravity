using System.Collections.Generic;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Compiler.Parsing;

/// <summary>
/// Result of <see cref="Parser.Parse"/>. <see cref="File"/> is <c>null</c> only when
/// a fatal lexical or syntactic error prevented any AST production.
/// </summary>
public sealed record ParseResult(SourceFile? File, IReadOnlyList<Diagnostic> Diagnostics);
