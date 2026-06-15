using FluentAssertions;
using Gravity.Dsl.Cli;
using Gravity.Dsl.Emitter.Sample.Outline;
using Xunit;

namespace Gravity.Dsl.Tests.Cli;

/// <summary>
/// Integration tests for the <c>gravc --plugin</c> flag introduced alongside
/// the external-emitter authoring surface. The flag accepts either a file path
/// to a single emitter assembly or a directory whose top-level <c>*.dll</c> files
/// are scanned. The library-level wiring lives on
/// <see cref="CompilerPipeline.Check"/> and <see cref="CompilerPipeline.Gen"/>'s
/// <c>extraEmitterAssemblies</c> parameter; <see cref="Program.Main"/> threads
/// the CLI surface through to it after expanding directories.
///
/// The reference plugin assembly used by the positive cases is the Outline sample
/// (<c>Gravity.Dsl.Emitter.Sample.Outline.dll</c>) because it is project-referenced
/// by this test project, which guarantees the on-disk DLL exists in the test bin
/// directory at the path <see cref="System.Reflection.Assembly.Location"/> points at.
/// </summary>
public sealed class PluginFlagTests
{
    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "cli_plugin");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("tests/fixtures/cli_plugin not found");
    }

    private static string OutlinePluginPath() =>
        typeof(OutlineEmitter).Assembly.Location;

    // (1) Library path: extraEmitterAssemblies threads the Outline emitter
    //     into CompilerPipeline.Gen's registry, so the run completes with
    //     both csharp and outline emitters active.
    [Fact]
    public async Task Gen_WithExtraEmitterAssembly_RegistersAndRunsThePlugin()
    {
        var input = FixtureRoot();
        var output = Path.Combine(Path.GetTempPath(), "gravity_plugin_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await CompilerPipeline.Gen(
                inputRoot: input,
                outputRoot: output,
                currentDate: new DateOnly(2026, 6, 15),
                emitterFilter: null,
                extraEmitterAssemblies: new[] { OutlinePluginPath() });
            result.Success.Should().BeTrue(
                because: "gen with the Outline plugin must succeed; got: "
                    + string.Join("; ", result.Diagnostics.Select(d => d.RuleId + " " + d.Message)));
            // Outline emits one .md per entity; the fixture declares Person.
            // The host writes under <output>/<outline-config-output>/, which
            // defaults to "outline" when no .gravity.yaml is present.
            var outlineDir = Path.Combine(output, "outline");
            Directory.Exists(outlineDir).Should().BeTrue(
                because: "the outline emitter must have produced its output directory");
            Directory.GetFiles(outlineDir, "*.md", SearchOption.AllDirectories)
                .Should().NotBeEmpty(
                    because: "the outline emitter must have written at least one Markdown file");
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    // (2) Library path: WITHOUT extraEmitterAssemblies, only the built-in
    //     csharp emitter is registered, so the outline output directory does
    //     not appear. This is the control case for (1).
    [Fact]
    public async Task Gen_WithoutExtraEmitterAssembly_DoesNotRunThePlugin()
    {
        var input = FixtureRoot();
        var output = Path.Combine(Path.GetTempPath(), "gravity_plugin_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await CompilerPipeline.Gen(
                inputRoot: input,
                outputRoot: output,
                currentDate: new DateOnly(2026, 6, 15),
                emitterFilter: null,
                extraEmitterAssemblies: null);
            result.Success.Should().BeTrue();
            Directory.Exists(Path.Combine(output, "outline")).Should().BeFalse(
                because: "without --plugin pointing at the Outline DLL, the outline emitter is not registered");
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    // (3) CLI path: Program.Main with --plugin <file> passes the assembly
    //     through to CompilerPipeline.Gen and the outline output appears.
    [Fact]
    public async Task ProgramMain_GenWithPluginFile_RegistersAndRunsThePlugin()
    {
        var input = FixtureRoot();
        var output = Path.Combine(Path.GetTempPath(), "gravity_plugin_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exit = await Program.Main(new[]
            {
                "gen",
                "--input", input,
                "--output", output,
                "--plugin", OutlinePluginPath(),
                "--as-of", "2026-06-15",
            });
            exit.Should().Be(0, because: "gen with --plugin pointing at the Outline DLL must exit 0");
            Directory.Exists(Path.Combine(output, "outline")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    // (4) CLI path: --plugin can point at a directory; every *.dll under
    //     the top level is enumerated. Verifies the file-or-directory
    //     expansion in Program.TryExpandPlugins.
    [Fact]
    public async Task ProgramMain_GenWithPluginDirectory_ScansAndRunsThePlugin()
    {
        var input = FixtureRoot();
        var output = Path.Combine(Path.GetTempPath(), "gravity_plugin_test_" + Guid.NewGuid().ToString("N"));
        var pluginDir = Path.Combine(Path.GetTempPath(), "gravity_plugin_dir_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(pluginDir);
            var dest = Path.Combine(pluginDir, Path.GetFileName(OutlinePluginPath()));
            File.Copy(OutlinePluginPath(), dest, overwrite: true);
            // The host loads dependencies from the source DLL's directory, so
            // running against an isolated plugin directory verifies the loader
            // resolves the IEmitter type identity correctly.

            int exit = await Program.Main(new[]
            {
                "gen",
                "--input", input,
                "--output", output,
                "--plugin", pluginDir,
                "--as-of", "2026-06-15",
            });
            exit.Should().Be(0, because: "gen with --plugin pointing at a directory of DLLs must exit 0");
            Directory.Exists(Path.Combine(output, "outline")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            if (Directory.Exists(pluginDir)) Directory.Delete(pluginDir, recursive: true);
        }
    }

    // (5) CLI path: a --plugin path that does not exist surfaces as CLI004
    //     on stderr and a non-zero exit. The check must fire before the
    //     registry is built so the user sees the bad-path message rather
    //     than a downstream "no registered target" warning.
    [Fact]
    public async Task ProgramMain_GenWithMissingPluginPath_ExitsNonZeroWithCli004()
    {
        var input = FixtureRoot();
        var output = Path.Combine(Path.GetTempPath(), "gravity_plugin_test_" + Guid.NewGuid().ToString("N"));
        var missing = Path.Combine(Path.GetTempPath(), "definitely_does_not_exist_" + Guid.NewGuid().ToString("N") + ".dll");

        var originalErr = Console.Error;
        var originalOut = Console.Out;
        var errBuf = new StringWriter();
        var outBuf = new StringWriter();
        try
        {
            Console.SetError(errBuf);
            Console.SetOut(outBuf);
            int exit = await Program.Main(new[]
            {
                "gen",
                "--input", input,
                "--output", output,
                "--plugin", missing,
            });
            exit.Should().NotBe(0);
            errBuf.ToString().Should().Contain("CLI004");
            errBuf.ToString().Should().Contain(missing);
        }
        finally
        {
            Console.SetError(originalErr);
            Console.SetOut(originalOut);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    // (6) Check path: extraEmitterAssemblies parameter is accepted and the
    //     plugin assembly loads cleanly. This is the wiring that lets
    //     Check's VAL006 namespace-claim check see plugin-claimed namespaces
    //     so the validator's view matches Gen's — the build-integration
    //     parity requirement (LD-11). A test that actually exercises VAL006
    //     with an annotated fixture would need a separate fixture; this
    //     test only locks the parameter wiring.
    [Fact]
    public async Task Check_AcceptsExtraEmitterAssembly_AndLoadsCleanly()
    {
        var input = FixtureRoot();
        var resultWithPlugin = await CompilerPipeline.Check(
            inputRoot: input,
            currentDate: new DateOnly(2026, 6, 15),
            emitterFilter: null,
            extraEmitterAssemblies: new[] { OutlinePluginPath() });
        resultWithPlugin.Success.Should().BeTrue();
        resultWithPlugin.Diagnostics.Should().NotContain(d => d.RuleId == "HOST001");
    }
}
