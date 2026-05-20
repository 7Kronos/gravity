using System.Collections.Immutable;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// AC-5.10 — cardinality drives the index strategy:
/// cardinality-one → FK + btree; cardinality-many → array column + GIN.
/// </summary>
public sealed class RelationIndexTests
{
    private static EmitterConfig DefaultConfig() => new(
        TargetName: "postgres-ddl", Enabled: true, Output: "postgres-ddl",
        Values: ImmutableSortedDictionary<string, object>.Empty
            .Add("output", "postgres-ddl")
            .Add("schema", "public")
            .Add("migration_prefix", "V"));

    [Fact]
    public void CardinalityOne_EmitsForeignKey_PlusBtreeIndex()
    {
        // TimeEntry has cardinality-one relations to Employee and Project.
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(model, DefaultConfig(), sink).Diagnostics.Should().BeEmpty();
        var sql = sink.Snapshot()["postgres-ddl/schema/hr/TimeEntry.sql"];

        // Column with NOT NULL (relations are non-optional in the sample).
        sql.Should().MatchRegex(@"employee_id UUID NOT NULL");
        // FK constraint wrapped in idempotency envelope.
        sql.Should().Contain("ADD CONSTRAINT fk_time_entry_employee_id");
        sql.Should().Contain("REFERENCES public.employee(id);");
        // Btree index — no USING clause.
        sql.Should().Contain("CREATE INDEX IF NOT EXISTS ix_time_entry_employee_id ON public.time_entry(employee_id);");
        // No GIN for a cardinality-one relation.
        sql.Should().NotContain("USING GIN (employee_id)");
    }

    [Fact]
    public void IndexStatementsAreSortedOrdinallyByName()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(model, DefaultConfig(), sink).Diagnostics.Should().BeEmpty();
        var sql = sink.Snapshot()["postgres-ddl/schema/hr/TimeEntry.sql"];

        int posEmployee = sql.IndexOf("ix_time_entry_employee_id");
        int posProject = sql.IndexOf("ix_time_entry_project_id");
        posEmployee.Should().BeGreaterThan(-1);
        posProject.Should().BeGreaterThan(-1);
        // Ordinal: "employee" < "project".
        posEmployee.Should().BeLessThan(posProject, because: "FR-453 — indexes sorted ordinally by name");
    }
}
