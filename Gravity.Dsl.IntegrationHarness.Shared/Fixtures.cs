namespace Gravity.Dsl.IntegrationHarness.Shared;

/// <summary>
/// Shared DSL source fixture constants used by both the xUnit fast lane and
/// the integration harness slow lane. Declaring them as <c>const string</c>
/// (not <c>static readonly string</c>) ensures compile-time string interning
/// so both consumers reference the identical bytes (FR-3002 / FR-3032 / AC-9c.3).
/// </summary>
public static class Fixtures
{
    /// <summary>
    /// Minimal well-formed Gravity DSL source declaring one <c>Employee</c> entity
    /// in the <c>hr</c> namespace. Used as the canonical integration fixture across
    /// AC-9.11..AC-9.15 and the smoke test. Verbatim copy of the const previously
    /// inlined at <c>MsBuildIntegrationTests.cs:84-109</c>.
    /// </summary>
    public const string MinimalEmployeeGravity =
        "namespace hr;\n"
        + "\n"
        + "entity Employee version 1 {\n"
        + "  identity id: UUID;\n"
        + "  properties {\n"
        + "    name: String;\n"
        + "  }\n"
        + "  lifecycle {\n"
        + "    states { Active, Terminated; }\n"
        + "    transitions { Active -> Terminated on Terminated; }\n"
        + "  }\n"
        + "  events {\n"
        + "    Terminated { terminated_at: DateTime; };\n"
        + "  }\n"
        + "  commands {\n"
        + "    Terminate(reason: String)\n"
        + "      returns TerminationResult\n"
        + "      with side_effect Terminated;\n"
        + "  }\n"
        + "}\n"
        + "\n"
        + "type TerminationResult {\n"
        + "  success: Boolean;\n"
        + "  message: String?;\n"
        + "}\n";

    /// <summary>
    /// Minimal intentionally-broken Gravity DSL source (a property with a missing
    /// type). Reserved for forward-compat use by a hypothetical AC-9.3 harness step;
    /// not consumed in Phase 9c but declared here per FR-3002.
    /// </summary>
    public const string MinimalBrokenGravity =
        "namespace hr;\n"
        + "\n"
        + "entity Foo version 1 { properties { x: ; } }\n";
}
