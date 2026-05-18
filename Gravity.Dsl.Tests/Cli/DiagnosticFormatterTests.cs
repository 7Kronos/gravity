using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Cli;
using Xunit;

namespace Gravity.Dsl.Tests.Cli;

/// <summary>
/// AC-7: every diagnostic surface emits in the canonical
/// <c>path:line:col: severity ruleId: message</c> format. The CLI uses
/// <see cref="DiagnosticFormatter.Format"/> for this; verify the format and
/// that resolver RES002 vs RES003 messages are distinct.
/// </summary>
public sealed class DiagnosticFormatterTests
{
    [Fact]
    public void Format_ProducesPathLineColSeverityRuleMessage()
    {
        var d = new Diagnostic(
            DiagnosticSeverity.Error,
            "VAL010",
            "relation 'x' may not combine '?' with 'cardinality many'",
            new SourceSpan("foo/bar.gravity", 42, 7, 0));

        DiagnosticFormatter.Format(d)
            .Should().Be("foo/bar.gravity:42:7: error VAL010: relation 'x' may not combine '?' with 'cardinality many'");
    }

    [Fact]
    public void Format_WarningSeverity_RendersAsLowercase()
    {
        var d = new Diagnostic(
            DiagnosticSeverity.Warning,
            "VAL005",
            "identity field 'id' is not UUID",
            new SourceSpan("E.gravity", 1, 1, 0));
        DiagnosticFormatter.Format(d).Should().Be("E.gravity:1:1: warning VAL005: identity field 'id' is not UUID");
    }

    [Fact]
    public async Task MissingImport_Res002_AndMissingDefinition_Res003_AreDistinct()
    {
        // Build a tiny project on disk with a file that imports a non-existent
        // path AND references an undeclared type. Phase 0-3 resolver produces
        // RES002 for the missing import and RES003 for the dangling reference.
        var tmp = Path.Combine(Path.GetTempPath(), "gravc-cli-" + System.Guid.Empty.ToString("N"));
        // Use a counter rather than Guid.NewGuid (banned) to avoid collisions.
        tmp = Path.Combine(Path.GetTempPath(), "gravc-cli-" + System.Threading.Interlocked.Increment(ref _tmpSeq));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "A.gravity"),
                "namespace x;\nimport \"NoSuchFile.gravity\";\n\nentity Thing version 1 {\n  identity id: UUID;\n  relations {\n    other: Missing cardinality one;\n  }\n  lifecycle {\n    states { S; }\n    transitions {}\n  }\n  events {}\n  commands {}\n}\n");

            var result = await CompilerPipeline.Check(tmp, default(System.DateOnly));
            result.Success.Should().BeFalse();
            var ids = result.Diagnostics.Select(d => d.RuleId).Distinct().ToArray();
            ids.Should().Contain("RES002", because: "missing import file");
            ids.Should().Contain("RES003", because: "undefined relation target");
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
        }
    }

    private static int _tmpSeq;
}
