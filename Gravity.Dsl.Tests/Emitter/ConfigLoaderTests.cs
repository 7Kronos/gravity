using System.Collections.Immutable;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Tests.Stubs;
using Xunit;

namespace Gravity.Dsl.Tests.Emitter;

public sealed class ConfigLoaderTests
{
    // A test-only emitter with a published schema so we can exercise CFG002 / CFG003.
    private sealed class SchemaStubEmitter : IEmitter
    {
        public string TargetName => "schema-stub";
        public string AnnotationNamespace => "";
        public SemanticVersionRange SupportedAstVersions { get; } = SemanticVersionRange.Parse(">=1.0.0 <2.0.0");
        public EmitterConfigSchema ConfigurationSchema { get; } = new(ImmutableArray.Create(
            new ConfigKey("namespace", ConfigValueKind.String, Required: false, Default: "Acme"),
            new ConfigKey("file_scoped_namespaces", ConfigValueKind.Bool, Required: false, Default: true),
            new ConfigKey("max_files", ConfigValueKind.Int, Required: false, Default: (long)0)
        ));

        public EmitResult Emit(Gravity.Dsl.Compiler.Resolution.ResolvedModel model, EmitterConfig config, IEmitterOutput sink)
            => new(ImmutableArray<Gravity.Dsl.Ast.Diagnostic>.Empty);
    }

    private static EmitterRegistry BuildRegistry() =>
        EmitterRegistry.FromInstances(new IEmitter[] { new SchemaStubEmitter() });

    [Fact]
    public void Cfg001_UnknownTopLevelKey_Warns()
    {
        var yaml = @"
unknown_top: 42
emitters:
  schema-stub:
    output: gen/schema
";
        var result = ConfigLoader.LoadFromString(yaml, ".gravity.config", BuildRegistry());
        result.Diagnostics.Should().Contain(d => d.RuleId == "CFG001" && d.Message.Contains("unknown_top"));
    }

    [Fact]
    public void Cfg002_TypeMismatch_Errors()
    {
        var yaml = @"
emitters:
  schema-stub:
    output: gen/schema
    file_scoped_namespaces: not-a-boolean
";
        var result = ConfigLoader.LoadFromString(yaml, ".gravity.config", BuildRegistry());
        result.Diagnostics.Should().Contain(d => d.RuleId == "CFG002" && d.Message.Contains("file_scoped_namespaces"));
    }

    [Fact]
    public void Cfg003_MissingRequiredOutput_Errors()
    {
        var yaml = @"
emitters:
  schema-stub:
    namespace: Acme
";
        var result = ConfigLoader.LoadFromString(yaml, ".gravity.config", BuildRegistry());
        result.Diagnostics.Should().Contain(d => d.RuleId == "CFG003" && d.Message.Contains("output"));
    }

    [Fact]
    public void Defaults_AreAppliedWhenKeyAbsent()
    {
        var yaml = @"
emitters:
  schema-stub:
    output: gen/schema
";
        var result = ConfigLoader.LoadFromString(yaml, ".gravity.config", BuildRegistry());
        result.Diagnostics.Should().BeEmpty();
        result.Configs.Should().ContainKey("schema-stub");
        var cfg = result.Configs["schema-stub"];
        cfg.Output.Should().Be("gen/schema");
        cfg.GetString("namespace").Should().Be("Acme");
        cfg.GetBool("file_scoped_namespaces").Should().BeTrue();
        cfg.GetInt("max_files").Should().Be(0);
    }

    [Fact]
    public void EnabledFlag_DefaultsToTrue_ButRespectsExplicitFalse()
    {
        var enabledYaml = @"
emitters:
  schema-stub:
    output: gen/schema
";
        var disabledYaml = @"
emitters:
  schema-stub:
    output: gen/schema
    enabled: false
";
        var enabled = ConfigLoader.LoadFromString(enabledYaml, ".gravity.config", BuildRegistry());
        enabled.Configs["schema-stub"].Enabled.Should().BeTrue();
        var disabled = ConfigLoader.LoadFromString(disabledYaml, ".gravity.config", BuildRegistry());
        disabled.Configs["schema-stub"].Enabled.Should().BeFalse();
    }

    [Fact]
    public void IntKey_CoercesIntegerLiteral()
    {
        var yaml = @"
emitters:
  schema-stub:
    output: gen/schema
    max_files: 5
";
        var result = ConfigLoader.LoadFromString(yaml, ".gravity.config", BuildRegistry());
        result.Diagnostics.Should().BeEmpty();
        result.Configs["schema-stub"].GetInt("max_files").Should().Be(5);
    }

    [Fact]
    public void UnknownEmitterKey_Warns_Cfg001()
    {
        var yaml = @"
emitters:
  schema-stub:
    output: gen/schema
    bogus_setting: 1
";
        var result = ConfigLoader.LoadFromString(yaml, ".gravity.config", BuildRegistry());
        result.Diagnostics.Should().Contain(d => d.RuleId == "CFG001" && d.Message.Contains("bogus_setting"));
    }

    [Fact]
    public void LoadFile_LegacyFilename_EmitsCfg005Deprecation()
    {
        // Load the same content from disk under the legacy and preferred filenames;
        // only the legacy filename should surface a CFG005 deprecation warning.
        var yaml = "emitters:\n  schema-stub:\n    output: gen/schema\n";
        var dir = Directory.CreateTempSubdirectory("gravity-cfg-tests-").FullName;
        try
        {
            var legacyPath = Path.Combine(dir, ConfigLoader.LegacyFileName);
            var preferredPath = Path.Combine(dir, ConfigLoader.PreferredFileName);
            File.WriteAllText(legacyPath, yaml);
            File.WriteAllText(preferredPath, yaml);

            var legacyResult = ConfigLoader.LoadFile(legacyPath, BuildRegistry());
            legacyResult.Diagnostics.Should().ContainSingle(d =>
                d.RuleId == "CFG005"
                && d.Severity == Gravity.Dsl.Ast.DiagnosticSeverity.Warning
                && d.Message.Contains(ConfigLoader.PreferredFileName));
            legacyResult.Configs.Should().ContainKey("schema-stub");

            var preferredResult = ConfigLoader.LoadFile(preferredPath, BuildRegistry());
            preferredResult.Diagnostics.Should().NotContain(d => d.RuleId == "CFG005");
            preferredResult.Configs.Should().ContainKey("schema-stub");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFile_LegacyFilename_MixedCase_StillEmitsCfg005()
    {
        // On Windows / default-case-insensitive macOS volumes the file system
        // resolves ".gravity.Config" to the same inode as ".gravity.config";
        // we want CFG005 to fire either way. On case-sensitive Linux this test
        // still asserts the loader handles the mixed-case literal we hand it
        // (the temp file is created with the mixed-case name verbatim).
        var yaml = "emitters:\n  schema-stub:\n    output: gen/schema\n";
        var dir = Directory.CreateTempSubdirectory("gravity-cfg-case-").FullName;
        try
        {
            var mixedCase = Path.Combine(dir, ".gravity.Config");
            File.WriteAllText(mixedCase, yaml);
            var result = ConfigLoader.LoadFile(mixedCase, BuildRegistry());
            result.Diagnostics.Should().Contain(d =>
                d.RuleId == "CFG005"
                && d.Severity == Gravity.Dsl.Ast.DiagnosticSeverity.Warning);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindInDirectory_PrefersYamlOverLegacyConfig()
    {
        var dir = Directory.CreateTempSubdirectory("gravity-cfg-probe-").FullName;
        try
        {
            // Neither present -> null.
            ConfigLoader.FindInDirectory(dir).Should().BeNull();

            // Only legacy present -> legacy.
            var legacy = Path.Combine(dir, ConfigLoader.LegacyFileName);
            File.WriteAllText(legacy, "emitters: {}\n");
            ConfigLoader.FindInDirectory(dir).Should().Be(legacy);

            // Both present -> preferred wins.
            var preferred = Path.Combine(dir, ConfigLoader.PreferredFileName);
            File.WriteAllText(preferred, "emitters: {}\n");
            ConfigLoader.FindInDirectory(dir).Should().Be(preferred);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
