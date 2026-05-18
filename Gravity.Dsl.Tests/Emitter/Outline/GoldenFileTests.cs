using Xunit;

namespace Gravity.Dsl.Tests.Emitter.Outline;

/// <summary>
/// T228 / T229 / AC-9.6 byte-checked golden files for the outline sample emitter.
/// Deferred to Phase 9b — see specs/003-phase-9-build-integration/tasks.md.
/// The structural coverage (six H2 sections per entity, determinism) is pinned by
/// RenderTests and DeterminismTests; byte-checked goldens would lock the markdown
/// shape and require hand-authoring tests/golden/outline/hr/{Employee,TimeEntry,Project}.md
/// against the existing samples/registry/ fixtures.
/// </summary>
public sealed class GoldenFileTests
{
    [Fact(Skip = "T228/T229 deferred to Phase 9b — tests/golden/outline/ files not yet authored. " +
                  "Structural coverage already pinned by RenderTests; byte-checked goldens are " +
                  "a Phase 9b deliverable per the implementation plan §6.1 deferral note.")]
    [Trait("Category", "Slow")]
    public void Outline_EmitsByteIdenticalToGolden()
    {
    }
}
