namespace Gravity.Dsl.Ast;

/// <summary>
/// One <c>From -&gt; To on EventName;</c> transition line inside an entity's
/// <c>lifecycle.transitions</c> block. See FR-030, FR-031.
/// </summary>
public sealed record TransitionDecl(string From, string To, string OnEvent, SourceSpan Span);
