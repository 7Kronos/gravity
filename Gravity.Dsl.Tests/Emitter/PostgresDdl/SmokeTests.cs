using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// AC-5.3 / AC-5.9 — end-to-end smoke against <c>samples/registry/</c>. Asserts
/// the emitter produces the expected file tree (3 entity tables + value-type
/// composites + enum types under <c>schema/</c>, plus V1 baseline migrations
/// under <c>migrations/</c>), zero diagnostics, and byte-identical output
/// across two runs.
/// </summary>
public sealed class SmokeTests
{
    private static EmitterConfig DefaultConfig() => new(
        TargetName: "postgres-ddl", Enabled: true, Output: "postgres-ddl",
        Values: ImmutableSortedDictionary<string, object>.Empty
            .Add("output", "postgres-ddl")
            .Add("schema", "public")
            .Add("migration_prefix", "V"));

    [Fact]
    public void EmitsTheExpectedFileTreeForTheHrRegistrySample()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        var cfg = DefaultConfig();

        var result = new PostgresDdlEmitter().Emit(model, cfg, sink);

        result.Diagnostics.Should().BeEmpty(
            because: "the registry sample contains no @postgres annotations and no v-mismatches");
        var snap = sink.Snapshot();

        // Schema/ tree: 3 entities + 15 value types (1 ContactInfo + 14 result types) + 2 enums = 20 schema files.
        // Migrations/ tree: V1 baseline per entity = 3.
        // Total: 23 files.
        var schemaFiles = snap.Keys.Where(k => k.StartsWith("postgres-ddl/schema/", System.StringComparison.Ordinal)).ToArray();
        var migrationFiles = snap.Keys.Where(k => k.StartsWith("postgres-ddl/migrations/", System.StringComparison.Ordinal)).ToArray();

        schemaFiles.Should().HaveCount(20);
        migrationFiles.Should().HaveCount(3);

        // Entity-table files present.
        snap.Keys.Should().Contain("postgres-ddl/schema/hr/Employee.sql");
        snap.Keys.Should().Contain("postgres-ddl/schema/hr/TimeEntry.sql");
        snap.Keys.Should().Contain("postgres-ddl/schema/hr/Project.sql");

        // V1 baselines present.
        snap.Keys.Should().Contain("postgres-ddl/migrations/hr/V1__Employee.sql");
        snap.Keys.Should().Contain("postgres-ddl/migrations/hr/V1__TimeEntry.sql");
        snap.Keys.Should().Contain("postgres-ddl/migrations/hr/V1__Project.sql");
    }

    [Fact]
    public void TwoRunsAreByteIdentical()
    {
        var model = SamplesLoader.LoadRegistry();
        var cfg = DefaultConfig();

        var firstSink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(model, cfg, firstSink).Diagnostics.Should().BeEmpty();
        var first = firstSink.Snapshot();

        var secondSink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(model, cfg, secondSink).Diagnostics.Should().BeEmpty();
        var second = secondSink.Snapshot();

        first.Keys.Should().Equal(second.Keys, because: "file set must be identical across runs (AC-5.9)");
        foreach (var key in first.Keys)
        {
            first[key].Should().Be(second[key],
                because: "file '" + key + "' must be byte-identical across runs");
        }
    }

    [Fact]
    public void EveryEmittedFileEndsWithExactlyOneLfNewline()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(model, DefaultConfig(), sink);

        foreach (var (path, body) in sink.Snapshot())
        {
            body.Should().NotBeEmpty(because: path + " must have content");
            body[body.Length - 1].Should().Be('\n', because: path + " must end with LF (FR-454)");
            body.Should().NotEndWith("\n\n", because: path + " must end with exactly one LF, not two");
            body.Should().NotContain("\r", because: path + " must use LF only, never CRLF (FR-454)");
        }
    }
}
