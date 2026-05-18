using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Validation;
using Xunit;

namespace Gravity.Dsl.Tests.Validation;

public sealed class ValidatorTests
{
    private static readonly HashSet<string> ClaimedNamespaces =
        new(System.StringComparer.Ordinal) { "csharp" };

    private static IReadOnlyList<Diagnostic> Run(string source, string path = "v.gravity")
    {
        var parsed = Parser.Parse(path, source);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture should parse: " + source);
        var resolve = Resolver.Resolve(new[] { parsed.File! }, System.IO.Directory.GetCurrentDirectory());
        resolve.Model.Should().NotBeNull(because: "fixture should resolve: " + source);
        return Validator.Validate(resolve.Model!, ClaimedNamespaces);
    }

    // ----- VAL001: transition state not in states {} -----

    [Fact]
    public void Val001_Negative_UnknownTransitionState_Errors()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    lifecycle {
        states { A; }
        transitions { A -> B on E; }
    }
    events { E {}; }
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Val001_Positive_AllStatesDeclared_NoError()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    lifecycle {
        states { A, B; }
        transitions { A -> B on E; }
    }
    events { E {}; }
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL001");
    }

    // ----- VAL002: transition event not in events {} -----

    [Fact]
    public void Val002_Negative_UnknownEvent_Errors()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    lifecycle {
        states { A, B; }
        transitions { A -> B on Phantom; }
    }
    events { E {}; }
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL002");
    }

    [Fact]
    public void Val002_Positive_DeclaredEvent_NoError()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    lifecycle {
        states { A, B; }
        transitions { A -> B on E; }
    }
    events { E {}; }
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL002");
    }

    // ----- VAL003: command side_effect event not in events {} -----

    [Fact]
    public void Val003_Negative_CommandUnknownSideEffect_Errors()
    {
        var src = @"
type R { ok: Boolean; }
entity X version 1 {
    identity id: UUID;
    events { E {}; }
    commands { Do() returns R with side_effect Missing; }
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL003");
    }

    [Fact]
    public void Val003_Positive_DeclaredSideEffect_NoError()
    {
        var src = @"
type R { ok: Boolean; }
entity X version 1 {
    identity id: UUID;
    events { E {}; }
    commands { Do() returns R with side_effect E; }
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL003");
    }

    // ----- VAL004: state with no incoming transition (warn, skip first state) -----

    [Fact]
    public void Val004_Negative_OrphanState_Warns()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    lifecycle {
        states { A, B; }
        transitions {}
    }
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL004" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Val004_Positive_NoOrphan_NoWarn()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    lifecycle {
        states { A, B; }
        transitions { A -> B on E; }
    }
    events { E {}; }
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL004");
    }

    // ----- VAL005: identity not UUID -> warn -----

    [Fact]
    public void Val005_Negative_NonUuidIdentity_Warns()
    {
        var src = @"
entity X version 1 {
    identity id: Int;
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL005" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Val005_Positive_UuidIdentity_NoWarn()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL005");
    }

    // ----- VAL006: annotation namespace not claimed -----

    [Fact]
    public void Val006_Negative_UnknownAnnotationNamespace_Errors()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    properties {
        name: String @rust(attr: ""x"");
    }
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL006");
    }

    [Fact]
    public void Val006_Positive_ClaimedAnnotationNamespace_NoError()
    {
        var src = @"
entity X version 1 {
    identity id: UUID;
    properties {
        name: String @csharp(attr: ""Display"");
    }
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL006");
    }

    // ----- VAL009: deprecates date format/calendar -----

    [Fact]
    public void Val009_Negative_BadDateFormat_Errors()
    {
        var src = @"
entity X version 2 deprecates version 1 until ""2026/12/31"" {
    identity id: UUID;
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL009");
    }

    [Fact]
    public void Val009_Negative_InvalidCalendarDate_Errors()
    {
        var src = @"
entity X version 2 deprecates version 1 until ""2026-13-40"" {
    identity id: UUID;
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL009");
    }

    [Fact]
    public void Val009_Positive_GoodDate_NoError()
    {
        var src = @"
entity X version 2 deprecates version 1 until ""2026-12-31"" {
    identity id: UUID;
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL009");
    }

    // ----- VAL010: '?' + cardinality many -----

    [Fact]
    public void Val010_Negative_OptionalManyRelation_Errors()
    {
        var src = @"
entity Y version 1 { identity id: UUID; }
entity X version 1 {
    identity id: UUID;
    relations { ys: Y? cardinality many; }
}";
        var diags = Run(src);
        diags.Should().Contain(d => d.RuleId == "VAL010" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Val010_Positive_PlainManyOrOptionalOne_NoError()
    {
        var src = @"
entity Y version 1 { identity id: UUID; }
entity X version 1 {
    identity id: UUID;
    relations {
        ys: Y cardinality many;
        z:  Y? cardinality one;
    }
}";
        var diags = Run(src);
        diags.Should().NotContain(d => d.RuleId == "VAL010");
    }
}
