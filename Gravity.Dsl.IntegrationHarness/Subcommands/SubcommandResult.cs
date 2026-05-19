namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// Value type returned by every <see cref="ISubcommand.Run"/> invocation.
/// Use the static factory methods <see cref="Pass"/> and <see cref="Fail"/> rather
/// than constructing directly.
/// </summary>
public sealed record SubcommandResult(
    bool Success,
    string? HarnessRuleId,
    string? FailureMessage,
    string? FixturePath,
    int? DotnetExitCode)
{
    /// <summary>Returns a successful result.</summary>
    public static SubcommandResult Pass() =>
        new(Success: true, HarnessRuleId: null, FailureMessage: null,
            FixturePath: null, DotnetExitCode: null);

    /// <summary>Returns a failure result.</summary>
    public static SubcommandResult Fail(
        string harnessRuleId,
        string failureMessage,
        string? fixturePath = null,
        int? dotnetExitCode = null) =>
        new(Success: false,
            HarnessRuleId: harnessRuleId,
            FailureMessage: failureMessage,
            FixturePath: fixturePath,
            DotnetExitCode: dotnetExitCode);
}
