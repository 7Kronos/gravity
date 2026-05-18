using System.Collections.Generic;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// Result of <see cref="IEmitter.Emit"/>. Emitters return diagnostics here rather
/// than throwing so the host can aggregate them, sort by
/// <c>(Path, Line, Column, RuleId)</c>, and propagate to the CLI deterministically.
/// </summary>
public sealed record EmitResult(IReadOnlyList<Diagnostic> Diagnostics);
