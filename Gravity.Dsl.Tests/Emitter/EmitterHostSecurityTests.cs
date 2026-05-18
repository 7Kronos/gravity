using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Tests.Helpers;
using Gravity.Dsl.Tests.Stubs;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter;

public sealed class EmitterHostSecurityTests
{
    private static (Gravity.Dsl.Compiler.Resolution.ResolvedModel Model, EmitterRegistry Registry) LoadModelAndRegistry()
    {
        var model = SamplesLoader.LoadRegistry();
        var registry = EmitterRegistry.FromInstances(new IEmitter[] { new NoopEmitter() });
        registry.Diagnostics.Should().BeEmpty();
        return (model, registry);
    }

    [Fact]
    public async Task Output_AbsolutePath_IsRejected_CFG004()
    {
        var (model, registry) = LoadModelAndRegistry();
        var rooted = System.OperatingSystem.IsWindows() ? "C:\\not-relative" : "/not-relative";
        var configs = new System.Collections.Generic.Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["noop"] = new EmitterConfig("noop", Enabled: true, Output: rooted,
                ImmutableSortedDictionary<string, object>.Empty.Add("output", rooted))
        };

        var outputRoot = Path.Combine(Path.GetTempPath(), "gravc-test-cfg004-abs");
        var result = await EmitterHost.Run(model, configs, registry, outputRoot);
        result.Diagnostics.Should().Contain(d => d.RuleId == "CFG004"
            && d.Message.Contains("must be a relative path"));
        result.EmitterBuffers.Should().BeEmpty(because: "pre-flight aborts before any emitter runs");
    }

    [Fact]
    public async Task Output_DotDotEscape_IsRejected_CFG004()
    {
        var (model, registry) = LoadModelAndRegistry();
        var configs = new System.Collections.Generic.Dictionary<string, EmitterConfig>(System.StringComparer.Ordinal)
        {
            ["noop"] = new EmitterConfig("noop", Enabled: true, Output: "../escape",
                ImmutableSortedDictionary<string, object>.Empty.Add("output", "../escape"))
        };

        var outputRoot = Path.Combine(Path.GetTempPath(), "gravc-test-cfg004-dotdot");
        var result = await EmitterHost.Run(model, configs, registry, outputRoot);
        result.Diagnostics.Should().Contain(d => d.RuleId == "CFG004"
            && d.Message.Contains("resolves outside the output root"));
        result.EmitterBuffers.Should().BeEmpty();
    }

    [Fact]
    public void WriteFile_AbsolutePath_Throws()
    {
        var sink = new BufferedEmitterOutput();
        var rooted = System.OperatingSystem.IsWindows() ? "C:\\absolute.cs" : "/absolute.cs";
        var act = () => sink.WriteFile(rooted, "// payload");
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*relative path required*");
    }

    [Fact]
    public void WriteFile_DotDotSegment_Throws()
    {
        var sink = new BufferedEmitterOutput();
        var act = () => sink.WriteFile("../escape.cs", "// payload");
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*relative path required*");
    }

    [Fact]
    public void WriteFile_NormalPath_Works()
    {
        var sink = new BufferedEmitterOutput();
        sink.WriteFile("subdir/ok.cs", "// payload");
        var snap = sink.Snapshot();
        snap.Should().ContainKey("subdir/ok.cs");
        snap["subdir/ok.cs"].Should().Be("// payload");
    }
}
