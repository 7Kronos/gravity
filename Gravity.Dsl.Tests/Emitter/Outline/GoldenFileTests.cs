using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.Sample.Outline;
using Gravity.Dsl.Tests.Helpers;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter.Outline;

/// <summary>
/// T228 / T229 / AC-9.6 byte-checked golden files for the outline sample emitter.
/// Mirrors <see cref="Gravity.Dsl.Tests.Emitter.CSharp.GoldenFileTests"/>: the
/// emitter runs against <c>samples/registry/</c> and each output file is
/// byte-compared to the corresponding file under <c>tests/golden/outline/</c>.
/// Set the environment variable <c>UPDATE_GOLDEN=1</c> to regenerate the goldens
/// after a deliberate emitter-output change (matches the Phase 8 mechanism).
/// </summary>
public sealed class GoldenFileTests
{
    private static ImmutableSortedDictionary<string, string> RunOutlineEmitter()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        var config = new EmitterConfig(
            TargetName: "outline",
            Enabled: true,
            Output: "outline",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "outline"));
        var result = new OutlineEmitter().Emit(model, config, sink);
        result.Diagnostics.Should().BeEmpty(
            because: "outline emitter must produce zero diagnostics on the registry sample");
        return sink.Snapshot();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Outline_EmitsByteIdenticalToGolden()
    {
        var goldenRoot = SamplesLoader.GoldenOutlineDir();
        var emitted = RunOutlineEmitter();

        // Map emitter buffer keys (prefixed with the emitter's configured output
        // "outline/...") to repo-relative golden paths under tests/golden/outline/.
        // The emitter's output prefix is "outline", so a buffer key of
        // "outline/hr/Employee.md" maps to golden "hr/Employee.md".
        var emittedRelative = new SortedDictionary<string, string>(StringComparer.Ordinal);
        const string prefix = "outline/";
        foreach (var kv in emitted)
        {
            var key = kv.Key;
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "outline emitter produced a file outside its configured output prefix: " + key);
            }
            emittedRelative[key.Substring(prefix.Length)] = kv.Value;
        }

        // Update mode: write the current output back to the golden tree. Triggered
        // by env var UPDATE_GOLDEN=1 (mirrors Phase 8 Validation/Phase8GoldenTests
        // and Phase 0–3 Emitter/CSharp/GoldenFileTests).
        if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_GOLDEN"), "1",
                StringComparison.Ordinal))
        {
            foreach (var kv in emittedRelative)
            {
                var goldenPath = Path.Combine(goldenRoot, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
                File.WriteAllText(goldenPath, kv.Value);
            }
            // Continue to byte-compare against the freshly-written goldens so the
            // test still asserts equality in update mode (matches Phase 8 idiom).
        }

        var goldens = Directory.GetFiles(goldenRoot, "*.md", SearchOption.AllDirectories);
        goldens.Should().NotBeEmpty(
            because: "tests/golden/outline must be populated (run with UPDATE_GOLDEN=1 to seed)");

        // Every golden must be produced and byte-equal.
        foreach (var goldenPath in goldens)
        {
            var rel = Path.GetRelativePath(goldenRoot, goldenPath).Replace('\\', '/');
            emittedRelative.Should().ContainKey(rel,
                because: "golden '" + rel + "' must be produced by the outline emitter");
            var goldenText = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            emittedRelative[rel].Should().Be(goldenText,
                because: "AC-9.6: golden '" + rel + "' must match outline emitter output byte-for-byte");
        }

        // No extras beyond the locked golden set.
        var goldenKeys = new HashSet<string>(
            goldens.Select(g => Path.GetRelativePath(goldenRoot, g).Replace('\\', '/')),
            StringComparer.Ordinal);
        var extras = emittedRelative.Keys.Where(k => !goldenKeys.Contains(k)).ToArray();
        extras.Should().BeEmpty(
            because: "outline emitter must not produce files outside the locked golden set");
    }
}
