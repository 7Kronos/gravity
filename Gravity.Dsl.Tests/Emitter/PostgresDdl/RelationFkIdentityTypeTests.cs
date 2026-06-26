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
/// A relation foreign-key column's PostgreSQL type must equal the TARGET
/// entity's identity column type: a relation to a String-identity entity emits
/// a <c>TEXT</c> FK column (a <c>UUID</c> FK over a <c>TEXT</c> PK would produce
/// invalid DDL); a relation to a UUID-identity entity stays <c>UUID</c>
/// (byte-identical to the legacy behaviour).
/// </summary>
public sealed class RelationFkIdentityTypeTests
{
    private static EmitterConfig Config() => new(
        TargetName: "postgres-ddl", Enabled: true, Output: "postgres-ddl",
        Values: ImmutableSortedDictionary<string, object>.Empty
            .Add("output", "postgres-ddl")
            .Add("schema", "public")
            .Add("migration_prefix", "V"));

    // Partner has a String identity; Org has a UUID identity; Account holds
    // cardinality-one FKs to both plus a cardinality-many FK to Partner.
    private const string Source =
@"namespace hr;

entity Partner version 1 {
  identity id: String;
  properties { name: String; }
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}

entity Org version 1 {
  identity id: UUID;
  properties { name: String; }
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}

entity Account version 1 {
  identity id: UUID;
  relations {
    partner: Partner cardinality one;
    org:     Org     cardinality one;
    members: Partner cardinality many;
  }
  properties { label: String; }
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}";

    private static ImmutableSortedDictionary<string, string> RenderSchema()
    {
        var parsed = Parser.Parse("Fixture.gravity", Source);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull();

        var sink = new BufferedEmitterOutput();
        new PostgresDdlEmitter().Emit(resolve.Model!, Config(), sink).Diagnostics.Should().BeEmpty();
        return sink.Snapshot();
    }

    [Fact]
    public void StringIdentityTarget_YieldsTextFkColumn_MatchingReferencedPk()
    {
        var snap = RenderSchema();
        var partner = snap["schema/hr/Partner.sql"];
        var account = snap["schema/hr/Account.sql"];

        // Partner's identity PK column is TEXT.
        partner.Should().Contain("id TEXT NOT NULL");
        partner.Should().Contain("PRIMARY KEY (id)");

        // The cardinality-one FK column matches that TEXT PK type, not UUID.
        account.Should().Contain("partner_id TEXT NOT NULL");
        account.Should().NotContain("partner_id UUID");
        account.Should().Contain("REFERENCES public.partner(id);");
    }

    [Fact]
    public void UuidIdentityTarget_StaysUuidFkColumn()
    {
        var snap = RenderSchema();
        var account = snap["schema/hr/Account.sql"];

        // Org has a UUID identity → the FK column remains UUID (unchanged).
        account.Should().Contain("org_id UUID NOT NULL");
        account.Should().Contain("REFERENCES public.org(id);");
    }

    [Fact]
    public void CardinalityMany_FkArrayElementType_FollowsTargetIdentity()
    {
        var snap = RenderSchema();
        var account = snap["schema/hr/Account.sql"];

        // members → Partner (String identity), so the array element type is TEXT.
        account.Should().Contain("members_ids TEXT[] NOT NULL DEFAULT '{}'::TEXT[]");
        account.Should().NotContain("members_ids UUID[]");
        // Cardinality-many uses a GIN index and skips the FK constraint.
        account.Should().Contain("USING GIN (members_ids)");
    }
}
