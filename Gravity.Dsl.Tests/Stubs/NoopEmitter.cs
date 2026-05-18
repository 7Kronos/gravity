using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;

namespace Gravity.Dsl.Tests.Stubs;

/// <summary>
/// No-op stub emitter used by host-level tests. Writes one file (<c>noop.txt</c>)
/// containing a sorted-by-name (ordinal) list of every <see cref="TopLevelDecl"/>
/// in the model. Claims no annotation namespace, supports AST <c>&gt;=1.0.0 &lt;2.0.0</c>.
/// </summary>
public sealed class NoopEmitter : IEmitter
{
    public string TargetName => "noop";
    public string AnnotationNamespace => string.Empty;
    public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
    public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;

    public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
    {
        var names = new List<string>();
        foreach (var kv in model.Declarations)
        {
            names.Add(kv.Value.Name);
        }
        names.Sort(StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var n in names)
        {
            sb.Append(n);
            sb.Append('\n');
        }
        sink.WriteFile("noop.txt", sb.ToString());

        return new EmitResult(ImmutableArray<Diagnostic>.Empty);
    }
}
