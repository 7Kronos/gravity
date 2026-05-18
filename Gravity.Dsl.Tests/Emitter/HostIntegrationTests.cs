using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Compiler.Parsing;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Tests.Stubs;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter;

public sealed class HostIntegrationTests
{
    private static string FindSamplesRegistryDir()
    {
        // Walk upward from the test assembly until we find samples/registry.
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "registry");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate samples/registry/ from test base directory");
    }

    private static ResolvedModel LoadRegistrySamples()
    {
        var registryDir = FindSamplesRegistryDir();
        var sources = Directory.GetFiles(registryDir, "*.gravity", SearchOption.TopDirectoryOnly);
        System.Array.Sort(sources, System.StringComparer.Ordinal);
        var files = new List<Gravity.Dsl.Ast.SourceFile>();
        foreach (var src in sources)
        {
            var parsed = Parser.Parse(src, File.ReadAllText(src));
            parsed.Diagnostics.Should().BeEmpty(because: "samples must parse cleanly: " + src);
            files.Add(parsed.File!);
        }
        var resolve = Resolver.Resolve(files, registryDir);
        resolve.Model.Should().NotBeNull();
        return resolve.Model!;
    }

    [Fact]
    public async Task NoopEmitter_TwoRuns_AreByteIdentical_Ac6a()
    {
        var model = LoadRegistrySamples();
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new NoopEmitter() });
        registry.Diagnostics.Should().BeEmpty();

        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["noop"] = new EmitterConfig(
                TargetName: "noop",
                Enabled: true,
                Output: "noop",
                Values: ImmutableSortedDictionary<string, object>.Empty.Add("output", "noop"))
        };

        var first = await EmitterHost.Run(model, configs, registry, outputRoot: null);
        var second = await EmitterHost.Run(model, configs, registry, outputRoot: null);

        first.Diagnostics.Should().BeEmpty();
        second.Diagnostics.Should().BeEmpty();

        var a = first.EmitterBuffers["noop"].Snapshot();
        var b = second.EmitterBuffers["noop"].Snapshot();

        a.Keys.Should().Equal(b.Keys);
        a["noop.txt"].Should().Be(b["noop.txt"], because: "AC-6a: in-process determinism");

        // Sanity-check the body: every TopLevelDecl name appears, sorted.
        var lines = a["noop.txt"].Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        var sorted = lines.OrderBy(x => x, System.StringComparer.Ordinal).ToArray();
        lines.Should().Equal(sorted);
        lines.Should().Contain("Employee");
        lines.Should().Contain("Project");
    }

    [Fact]
    public async Task Host003_OverlappingOutputDirectories_AbortsRun()
    {
        var model = LoadRegistrySamples();
        var registry = EmitterRegistry.FromInstances(new IEmitter[]
        {
            new NoopEmitter(),
            new SecondNoopEmitter()
        });
        registry.Diagnostics.Should().BeEmpty();

        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["noop"] = new EmitterConfig("noop", Enabled: true, Output: "gen/shared",
                ImmutableSortedDictionary<string, object>.Empty.Add("output", "gen/shared")),
            ["noop-two"] = new EmitterConfig("noop-two", Enabled: true, Output: "gen/shared",
                ImmutableSortedDictionary<string, object>.Empty.Add("output", "gen/shared"))
        };

        var result = await EmitterHost.Run(model, configs, registry, outputRoot: null);
        result.Diagnostics.Should().Contain(d => d.RuleId == "HOST003"
            && d.Message.Contains("noop") && d.Message.Contains("noop-two"));
        // Pre-flight aborts before any emitter runs, so buffers are empty.
        result.EmitterBuffers.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitTo_WritesUtf8NoBom_LfLineEndings()
    {
        var model = LoadRegistrySamples();
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new NoopEmitter() });

        var configs = new Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["noop"] = new EmitterConfig("noop", Enabled: true, Output: "out",
                ImmutableSortedDictionary<string, object>.Empty.Add("output", "out"))
        };

        var tempRoot = Path.Combine(Path.GetDirectoryName(typeof(HostIntegrationTests).Assembly.Location)!,
            "host-int-" + System.Threading.Interlocked.Increment(ref _seq));
        try
        {
            var run = await EmitterHost.Run(model, configs, registry, outputRoot: tempRoot);
            run.Diagnostics.Should().BeEmpty();
            var written = Path.Combine(tempRoot, "out", "noop.txt");
            File.Exists(written).Should().BeTrue();
            var bytes = File.ReadAllBytes(written);
            // No BOM.
            (bytes.Length >= 3
                && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                .Should().BeFalse(because: "UTF-8 BOM is forbidden");
            // No CR.
            bytes.Should().NotContain((byte)0x0D);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static int _seq;

    /// <summary>Second no-op emitter used by HOST003 test (distinct TargetName).</summary>
    private sealed class SecondNoopEmitter : IEmitter
    {
        private readonly NoopEmitter _inner = new();
        public string TargetName => "noop-two";
        public string AnnotationNamespace => "";
        public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
        public EmitterConfigSchema ConfigurationSchema => EmitterConfigSchema.Empty;
        public EmitResult Emit(ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
            => _inner.Emit(model, config, sink);
    }
}
