using System.Collections.Immutable;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.PostgresDdl;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.PostgresDdl;

/// <summary>
/// AC-5.11 — <c>@postgres</c> claimed keys fold onto column DDL verbatim.
/// AC-5.12 — invalid annotation keys/values raise <c>PG003</c> / <c>PG004</c>.
/// </summary>
public sealed class AnnotationTests
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
    public void PostgresColumnOverride_RenamesEmittedColumn()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                email: String @postgres(column: "primary_email");
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();
        var sql = files["out/schema/x/Doc.sql"];
        sql.Should().Contain("primary_email TEXT NOT NULL");
        // The original `email` identifier should not survive as a bare column declaration.
        sql.Should().NotContain("\n    email TEXT");
    }

    [Fact]
    public void PostgresUnique_AppendsUniqueConstraint()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                email: String @postgres(unique: true);
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();
        files["out/schema/x/Doc.sql"].Should().Contain("email TEXT NOT NULL UNIQUE");
    }

    [Fact]
    public void PostgresIndex_True_EmitsBtreeIndex()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                code: String @postgres(index: true);
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();
        files["out/schema/x/Doc.sql"].Should().Contain("CREATE INDEX IF NOT EXISTS ix_doc_code ON public.doc(code);");
    }

    [Fact]
    public void PostgresDefault_AppendsDefaultExpressionVerbatim()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                code: String @postgres(default: "'EMP-' || nextval('seq')");
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().BeEmpty();
        files["out/schema/x/Doc.sql"].Should().Contain("DEFAULT 'EMP-' || nextval('seq')");
    }

    [Fact]
    public void UnknownPostgresKey_RaisesPg003()
    {
        var (_, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                code: String @postgres(unknown_key: "x");
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().ContainSingle().Which.RuleId.Should().Be("PG003");
        result.Diagnostics[0].Message.Should().Contain("unknown @postgres key").And.Contain("unknown_key");
    }

    [Fact]
    public void ColumnOverride_NotString_RaisesPg003()
    {
        var (_, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                code: String @postgres(column: 42);
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().ContainSingle().Which.RuleId.Should().Be("PG003");
        result.Diagnostics[0].Message.Should().Contain("column").And.Contain("expected string");
    }

    [Fact]
    public void ColumnOverride_InvalidIdentifier_RaisesPg004()
    {
        var (_, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                code: String @postgres(column: "1bad");
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().ContainSingle().Which.RuleId.Should().Be("PG004");
        result.Diagnostics[0].Message.Should().Contain("'1bad'").And.Contain("not a valid PostgreSQL identifier");
    }

    [Fact]
    public void ReservedKey_Precision_RaisesPg003()
    {
        var (_, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                cost: Decimal @postgres(precision: 10);
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().ContainSingle().Which.RuleId.Should().Be("PG003");
        result.Diagnostics[0].Message.Should().Contain("precision").And.Contain("reserved");
    }

    [Fact]
    public void UniqueKey_NotBoolean_RaisesPg003()
    {
        var (_, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                code: String @postgres(unique: "yes");
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().ContainSingle().Which.RuleId.Should().Be("PG003");
        result.Diagnostics[0].Message.Should().Contain("unique").And.Contain("expected boolean");
    }

    [Fact]
    public void PropertyNamedState_RaisesPg002_AndSkipsEntity()
    {
        var (files, result) = Emit("""
            namespace x;
            entity Doc version 1 {
              identity id: UUID;
              properties {
                state: String;
              }
              lifecycle { states { Active; } transitions { } }
              events { }
              commands { }
            }
            """);
        result.Diagnostics.Should().ContainSingle().Which.RuleId.Should().Be("PG002");
        // Skipping the entity drops the schema/ entry too.
        files.Should().NotContainKey("out/schema/x/Doc.sql");
    }
}
