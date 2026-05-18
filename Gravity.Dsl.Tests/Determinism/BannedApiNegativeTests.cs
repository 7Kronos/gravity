using Xunit;

namespace Gravity.Dsl.Tests.Determinism;

/// <summary>
/// Negative test for the banned-API analyzer wired in <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// <para>
/// Programmatic verification would need a separate Roslyn-based test harness that
/// (a) loads <c>Microsoft.CodeAnalysis.BannedApiAnalyzers</c>, (b) compiles a
/// synthetic snippet calling a banned member, and (c) asserts that diagnostic
/// <c>RS0030</c> shows up. That harness pulls Roslyn analyzer dependencies into
/// the test project, which the Phase 2 lockfile does not include.
/// </para>
/// <para>
/// In practice the analyzer's behavior is exercised end-to-end by the regular
/// <c>dotnet build</c> with <c>TreatWarningsAsErrors=true</c>: if a contributor
/// adds a banned call to any non-test project the build fails. A dedicated CI
/// step (deferred) will re-introduce a snippet known to call a banned API into a
/// throwaway project and assert the build fails.
/// </para>
/// <para>
/// This test is left in the suite (skipped) so the gap is visible. Remove the
/// <c>Skip</c> attribute and add the Roslyn-based harness when CI catches up.
/// </para>
/// </remarks>
public sealed class BannedApiNegativeTests
{
    [Fact(Skip = "deferred to CI: Roslyn-programmatic analyzer harness not in Phase 2 scope")]
    public void BannedSymbol_ProducesDiagnostic_Rs0030()
    {
        // TODO(Phase 3+): build a tiny CSharpCompilation in-process with
        // BannedApiAnalyzers + a snippet calling System.DateTime.Now, then
        // assert diagnostics contains RS0030.
    }
}
