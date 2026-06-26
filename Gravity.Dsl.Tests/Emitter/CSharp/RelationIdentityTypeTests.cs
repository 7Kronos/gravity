using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.CSharp;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.CSharp;

/// <summary>
/// A relation is a foreign key to the TARGET entity's identity, so its C# type must
/// follow the target identity's C# type (UUID → <c>Guid</c>, String → <c>string</c>),
/// not a hardcoded <c>Guid</c>. The <c>using System;</c> directive must follow the
/// actual rendered FK element types so the emitted file always compiles.
/// </summary>
public sealed class RelationIdentityTypeTests
{
    /// <summary>
    /// Render a multi-entity source and return the C# file ending in
    /// <paramref name="entityFileSuffix"/> (e.g. <c>B.cs</c>).
    /// </summary>
    private static async Task<string> RenderFile(string src, string entityFileSuffix)
    {
        var parsed = Parser.Parse("Fixture.gravity", src);
        parsed.Diagnostics.Should().BeEmpty(because: "fixture must parse cleanly");
        var resolve = Resolver.Resolve(new[] { parsed.File! }, inputRoot: "/tmp");
        resolve.Diagnostics.Should().BeEmpty(because: "fixture must resolve cleanly");
        resolve.Model.Should().NotBeNull();

        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["csharp"] = new EmitterConfig(
                TargetName: "csharp", Enabled: true, Output: "gen/csharp",
                Values: ImmutableSortedDictionary<string, object>.Empty
                    .Add("output", "gen/csharp")
                    .Add("namespace", "AcmeCo.Domain")
                    .Add("file_scoped_namespaces", true)),
        };
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new CSharpEmitter() });
        var run = await EmitterHost.Run(resolve.Model!, configs, registry, outputRoot: null);
        run.Diagnostics.Should().BeEmpty();
        var buffers = run.EmitterBuffers["csharp"].Snapshot();
        return buffers.Single(kv => kv.Key.EndsWith(entityFileSuffix, System.StringComparison.Ordinal)).Value;
    }

    private const string StringIdentityModel = @"namespace hr;
entity A version 1 {
  identity id: String;
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}
entity B version 1 {
  identity id: String;
  relations {
    a:  A cardinality one;
    ao: A? cardinality one;
    as: A cardinality many;
  }
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}";

    private const string UuidIdentityModel = @"namespace hr;
entity C version 1 {
  identity id: UUID;
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}
entity D version 1 {
  identity id: String;
  relations {
    c:  C cardinality one;
    cs: C cardinality many;
  }
  lifecycle { states { Active; } transitions {} }
  events {}
  commands {}
}";

    [Fact]
    public async Task StringIdentityTarget_RendersStringForeignKeys()
    {
        var cs = await RenderFile(StringIdentityModel, "B.cs");
        cs.Should().Contain("string A,");
        cs.Should().Contain("string? Ao,");
        cs.Should().Contain("ImmutableArray<string> As,");
        cs.Should().NotContain("Guid");
    }

    [Fact]
    public async Task StringOnlyEntity_OmitsUsingSystem()
    {
        // B has a String identity and only String-identity FKs — no Guid/DateTime/
        // DateOnly appears, so the file must NOT include `using System;`.
        var cs = await RenderFile(StringIdentityModel, "B.cs");
        cs.Should().NotContain("using System;");
        // The many relation still needs the Immutable using.
        cs.Should().Contain("using System.Collections.Immutable;");
    }

    [Fact]
    public async Task UuidIdentityTarget_RendersGuidForeignKeys()
    {
        var cs = await RenderFile(UuidIdentityModel, "D.cs");
        cs.Should().Contain("Guid C,");
        cs.Should().Contain("ImmutableArray<Guid> Cs,");
    }

    [Fact]
    public async Task GuidForeignKey_PullsInUsingSystem()
    {
        // D's identity is String (no System on its own), but it references a
        // UUID-identity target, so the rendered Guid FK must add `using System;`.
        var cs = await RenderFile(UuidIdentityModel, "D.cs");
        cs.Should().Contain("using System;");
    }
}
