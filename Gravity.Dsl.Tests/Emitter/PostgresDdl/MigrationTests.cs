using System.Collections.Immutable;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// AC-5.8 — per-entity-version migration ledger. V1 is the full baseline;
/// V&lt;N&gt; carries forward-only ALTER statements for new properties /
/// relations / lifecycle states. No DROP, no ALTER COLUMN TYPE.
/// </summary>
public sealed class MigrationTests
{
    private static (ImmutableSortedDictionary<string, string> Files, EmitResult Result) Emit(string source)
    {
        var parsed = Parser.Parse("test.gravity", source);
        parsed.Diagnostics.Should().BeEmpty(because: "test source must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Model.Should().NotBeNull(because: "test source must resolve cleanly");
        var sink = new BufferedEmitterOutput();
        var cfg = new EmitterConfig("postgres-ddl", true, "postgres-ddl",
            ImmutableSortedDictionary<string, object>.Empty.Add("output", "out"));
        var result = new PostgresDdlEmitter().Emit(resolve.Model!, cfg, sink);
        return (sink.Snapshot(), result);
    }

    [Fact]
    public void TwoVersions_ProduceVersionSuffixedSchema_PlusVnMigrations()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                title: String;
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            entity Doc version 2 {
              identity id: UUID;
              properties {
                title: String;
                summary: String?;
                published_at: DateTime;
              }
              lifecycle { states { Active, Archived; } transitions { Active -> Archived on Archive; } }
              events { Archive { at: DateTime; }; }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();

        // FR-425: multi-version → .v<N>.sql suffix everywhere.
        files.Should().ContainKey("out/schema/x/Doc.v1.sql");
        files.Should().ContainKey("out/schema/x/Doc.v2.sql");
        files.Should().NotContainKey("out/schema/x/Doc.sql");

        // V1 baseline + V2 diff.
        files.Should().ContainKey("out/migrations/x/V1__Doc.sql");
        files.Should().ContainKey("out/migrations/x/V2__Doc.sql");
    }

    [Fact]
    public void V2Migration_OnlyEmitsAdditiveStatementsForNewMembers()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties { title: String; }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            entity Doc version 2 {
              identity id: UUID;
              properties {
                title: String;
                summary: String?;
                published_at: DateTime;
              }
              lifecycle { states { Active, Archived; } transitions { Active -> Archived on Archive; } }
              events { Archive { at: DateTime; }; }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();

        var v2 = files["out/migrations/x/V2__Doc.sql"];
        // New properties.
        v2.Should().Contain("ALTER TABLE public.doc_v2 ADD COLUMN IF NOT EXISTS summary TEXT;");
        v2.Should().Contain("ALTER TABLE public.doc_v2 ADD COLUMN IF NOT EXISTS published_at TIMESTAMPTZ NOT NULL;");
        // New lifecycle state.
        v2.Should().Contain("ALTER TYPE public.doc_v2_state ADD VALUE IF NOT EXISTS 'Archived';");

        // No DROP / ALTER COLUMN TYPE anywhere.
        v2.Should().NotContainAny("DROP COLUMN", "DROP TABLE", "ALTER COLUMN", "DROP TYPE");
    }

    [Fact]
    public void V1Migration_IsTheFullBaselineDdl()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties { title: String; }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            entity Doc version 2 {
              identity id: UUID;
              properties { title: String; summary: String?; }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();

        // V1 migration should contain the full CREATE TABLE.
        var v1 = files["out/migrations/x/V1__Doc.sql"];
        v1.Should().Contain("CREATE TABLE IF NOT EXISTS public.doc_v1");
        v1.Should().Contain("title TEXT NOT NULL");
        v1.Should().Contain("PRIMARY KEY (id)");
    }
}
