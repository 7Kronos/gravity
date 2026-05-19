using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.JsonSchema;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.11 — byte-checked golden files for the JSON Schema emitter. Mirrors
/// the existing C# / outline emitter golden test harness: the emitter runs
/// against <c>samples/registry/</c> and each output file is byte-compared to
/// the corresponding file under <c>tests/golden/json-schema/registry/</c>.
/// Set <c>UPDATE_GOLDEN=1</c> to regenerate after a deliberate output change.
/// </summary>
public sealed class GoldenFileTests
{
    private static ImmutableSortedDictionary<string, string> RunJsonSchemaEmitter()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        var config = new EmitterConfig(
            TargetName: "json-schema", Enabled: true, Output: "json-schema",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "json-schema"));
        var result = new JsonSchemaEmitter().Emit(model, config, sink);
        result.Diagnostics.Should().BeEmpty(
            because: "json-schema emitter must produce zero diagnostics on the registry sample");
        return sink.Snapshot();
    }

    private static string GoldenJsonSchemaRegistryDir() =>
        SamplesLoader.FindRepoSubdirectory(Path.Combine("tests", "golden", "json-schema", "registry"));

    [Fact]
    [Trait("Category", "Slow")]
    public void JsonSchema_EmitsByteIdenticalToGolden()
    {
        var goldenRoot = GoldenJsonSchemaRegistryDir();
        var emitted = RunJsonSchemaEmitter();

        // The emitter's output prefix is "json-schema", so a buffer key of
        // "json-schema/hr/Employee.json" maps to golden "hr/Employee.json".
        var emittedRelative = new SortedDictionary<string, string>(StringComparer.Ordinal);
        const string prefix = "json-schema/";
        foreach (var kv in emitted)
        {
            kv.Key.StartsWith(prefix, StringComparison.Ordinal).Should().BeTrue(
                because: "json-schema emitter must keep all output under its configured prefix: " + kv.Key);
            emittedRelative[kv.Key.Substring(prefix.Length)] = kv.Value;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_GOLDEN"), "1", StringComparison.Ordinal))
        {
            foreach (var kv in emittedRelative)
            {
                var goldenPath = Path.Combine(goldenRoot, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
                File.WriteAllText(goldenPath, kv.Value);
            }
        }

        var goldens = Directory.GetFiles(goldenRoot, "*.json", SearchOption.AllDirectories);
        goldens.Should().NotBeEmpty(
            because: "tests/golden/json-schema/registry must be populated (run with UPDATE_GOLDEN=1 to seed)");

        foreach (var goldenPath in goldens)
        {
            var rel = Path.GetRelativePath(goldenRoot, goldenPath).Replace('\\', '/');
            emittedRelative.Should().ContainKey(rel,
                because: "golden '" + rel + "' must be produced by the json-schema emitter");
            var goldenText = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            emittedRelative[rel].Should().Be(goldenText,
                because: "AC-4.11: golden '" + rel + "' must match json-schema emitter output byte-for-byte");
        }

        var goldenKeys = new HashSet<string>(
            goldens.Select(g => Path.GetRelativePath(goldenRoot, g).Replace('\\', '/')),
            StringComparer.Ordinal);
        var extras = emittedRelative.Keys.Where(k => !goldenKeys.Contains(k)).ToArray();
        extras.Should().BeEmpty(
            because: "json-schema emitter must not produce files outside the locked golden set");

        // AC-4.3: file count is exactly 20 (3 entities + 15 value types + 2 enums).
        emittedRelative.Should().HaveCount(20);
    }
}
