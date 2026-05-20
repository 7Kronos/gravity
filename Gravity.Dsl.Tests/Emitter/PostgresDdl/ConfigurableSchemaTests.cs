using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// AC-5.7 — configurable target schema propagates through every emitted
/// statement; invalid identifiers produce <c>PG001</c> and no output.
/// </summary>
public sealed class ConfigurableSchemaTests
{
    private static EmitterConfig ConfigWithSchema(string schema) => new(
        TargetName: "postgres-ddl", Enabled: true, Output: "postgres-ddl",
        Values: ImmutableSortedDictionary<string, object>.Empty
            .Add("output", "postgres-ddl")
            .Add("schema", schema)
            .Add("migration_prefix", "V"));

    [Theory]
    [InlineData("public")]
    [InlineData("hr_prod")]
    [InlineData("tenant_42")]
    public void EveryDdlStatement_CarriesTheConfiguredSchemaQualifier(string schema)
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(model, ConfigWithSchema(schema), sink).Diagnostics.Should().BeEmpty();

        var employee = sink.Snapshot()["postgres-ddl/schema/hr/Employee.sql"];
        employee.Should().Contain("CREATE TABLE IF NOT EXISTS " + schema + ".employee");
        employee.Should().Contain("CREATE TYPE " + schema + ".employee_state");
    }

    [Fact]
    public void InvalidSchemaName_RaisesPg001_AndProducesNoOutput()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        var result = new PostgresDdlEmitter().Emit(model, ConfigWithSchema("1bad"), sink);

        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].RuleId.Should().Be("PG001");
        result.Diagnostics[0].Severity.Should().Be(Gravity.Dsl.Ast.DiagnosticSeverity.Error);
        result.Diagnostics[0].Message.Should().Contain("'1bad'").And.Contain("not a valid PostgreSQL identifier");

        sink.Snapshot().Should().BeEmpty(
            because: "PG001 short-circuits emission entirely — no schema/, no migrations/");
    }

    [Fact]
    public void DefaultSchemaIsPublic_WhenSchemaKeyIsOmitted()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        // Omit `schema` from the values dictionary — host's ConfigLoader would
        // have applied the default at validation time, but the emitter must
        // also fall through to its own default if the key is missing.
        var cfg = new EmitterConfig(
            TargetName: "postgres-ddl", Enabled: true, Output: "postgres-ddl",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "postgres-ddl"));
        new PostgresDdlEmitter().Emit(model, cfg, sink).Diagnostics.Should().BeEmpty();
        sink.Snapshot()["postgres-ddl/schema/hr/Employee.sql"]
            .Should().Contain("CREATE TABLE IF NOT EXISTS public.employee");
    }
}
