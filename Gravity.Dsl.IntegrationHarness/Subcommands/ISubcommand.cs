namespace Gravity.Dsl.IntegrationHarness.Subcommands;

/// <summary>
/// Contract for a single per-AC integration-harness subcommand (FR-3010..FR-3015).
/// Each implementation covers one acceptance criterion and is invoked by
/// <see cref="HarnessRunner"/> either directly (single-subcommand mode) or as
/// part of <c>run-all</c>.
/// </summary>
public interface ISubcommand
{
    /// <summary>The argv token used to invoke this subcommand (e.g. <c>run-ac-9.11</c>).</summary>
    string SubcommandName { get; }

    /// <summary>The AC identifier emitted in the pass/fail stdout line (e.g. <c>9.11</c>).</summary>
    string AcId { get; }

    /// <summary>
    /// Executes the subcommand's assertion logic.
    /// </summary>
    /// <param name="scratchDir">
    /// Per-invocation scratch directory allocated by <c>ScratchDir.For(...)</c>.
    /// The subcommand owns this directory and all temporary files within it.
    /// </param>
    /// <param name="workspaceRoot">
    /// Absolute path to the repository root (located by walking up from
    /// <c>AppContext.BaseDirectory</c> looking for <c>Gravity.Dsl.sln</c>).
    /// </param>
    /// <param name="log">Per-step log writer; use to record diagnostic detail.</param>
    /// <returns>A <see cref="SubcommandResult"/> describing pass or fail.</returns>
    SubcommandResult Run(string scratchDir, string workspaceRoot, HarnessLog log);
}
