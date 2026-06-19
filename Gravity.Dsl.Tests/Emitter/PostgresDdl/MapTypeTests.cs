using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// Map (dictionary) → PostgreSQL rendering. A <c>Map&lt;K, V&gt;</c> column is
/// stored as a single <c>jsonb</c> value; optionality follows the usual
/// NULL/NOT NULL convention.
/// </summary>
public sealed class MapTypeTests
{
    private static EmitterConfig Config() => new(
        TargetName: "postgres-ddl", Enabled: true, Output: "postgres-ddl",
        Values: ImmutableSortedDictionary<string, object>.Empty
            .Add("output", "postgres-ddl")
            .Add("schema", "public")
            .Add("migration_prefix", "V"));

    private static string RenderEntityTable(string propertyDecl)
    {
        var src =
$@"namespace hr;
entity F version 1 {{
  identity id: UUID;
  properties {{
    {propertyDecl};
  }}
  lifecycle {{
    states {{ Active; }}
    transitions {{}}
  }}
  events {{}}
  commands {{}}
}}";
        var parsed = Parser.Parse("Fixture.gravity", src);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull();

        var sink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(resolve.Model!, Config(), sink).Diagnostics.Should().BeEmpty();
        return sink.Snapshot().Single(kv => kv.Key.EndsWith("schema/hr/F.sql", System.StringComparison.Ordinal)).Value;
    }

    [Fact]
    public void RequiredMap_IsNotNullJsonbColumn()
    {
        var sql = RenderEntityTable("external_ids: Map<String, String>");
        sql.Should().Contain("external_ids JSONB NOT NULL");
    }

    [Fact]
    public void OptionalMap_IsNullableJsonbColumn()
    {
        var sql = RenderEntityTable("external_ids: Map<String, String>?");
        sql.Should().Contain("external_ids JSONB");
        sql.Should().NotContain("external_ids JSONB NOT NULL");
    }

    [Fact]
    public void ArrayOfMap_IsJsonbArrayColumn()
    {
        // The [] modifier produces a real PG array of JSONB, mirroring the
        // primitive/named array convention (e.g. INTEGER[]) and keeping
        // Map<K,V> distinguishable from Map<K,V>[] across emitters.
        var sql = RenderEntityTable("external_ids: Map<String, String>[]");
        sql.Should().Contain("external_ids JSONB[] NOT NULL");
    }
}
