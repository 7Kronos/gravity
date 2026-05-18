using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using Gravity.Dsl.Ast;
using YamlDotNet.RepresentationModel;

namespace Gravity.Dsl.Emitter;

/// <summary>
/// Parses <c>.gravity.config</c> YAML into one <see cref="EmitterConfig"/> per
/// emitter target named in the file. The loader is schema-driven: each emitter
/// section is validated against the corresponding <see cref="IEmitter.ConfigurationSchema"/>,
/// producing <c>CFG001</c> for unknown top-level keys, <c>CFG002</c> for type
/// mismatches, and <c>CFG003</c> for missing required keys.
/// </summary>
public static class ConfigLoader
{
    /// <summary>Result of <see cref="LoadFromString"/> / <see cref="LoadFile"/>.</summary>
    public sealed record LoadResult(
        ImmutableSortedDictionary<string, EmitterConfig> Configs,
        ImmutableArray<Diagnostic> Diagnostics);

    private static readonly HashSet<string> KnownTopLevelKeys =
        new(StringComparer.Ordinal) { "emitters" };

    /// <summary>Convenience overload that reads <paramref name="path"/> from disk.</summary>
    public static LoadResult LoadFile(string path, EmitterRegistry registry)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var text = File.ReadAllText(path);
        return LoadFromString(text, path, registry);
    }

    /// <summary>Parse <paramref name="yaml"/>; <paramref name="sourcePath"/> is used only for diagnostic spans.</summary>
    public static LoadResult LoadFromString(string yaml, string sourcePath, EmitterRegistry registry)
    {
        if (yaml is null) throw new ArgumentNullException(nameof(yaml));
        if (sourcePath is null) throw new ArgumentNullException(nameof(sourcePath));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var configs = ImmutableSortedDictionary.CreateBuilder<string, EmitterConfig>(StringComparer.Ordinal);

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Cfg002,
                "could not parse .gravity.config YAML: " + ex.Message,
                new SourceSpan(sourcePath, 1, 1, 0)));
            return new LoadResult(configs.ToImmutable(), diagnostics.ToImmutable());
        }
        if (stream.Documents.Count == 0)
        {
            return new LoadResult(configs.ToImmutable(), diagnostics.ToImmutable());
        }

        var root = stream.Documents[0].RootNode;
        if (root is not YamlMappingNode rootMap)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Cfg002,
                ".gravity.config root must be a YAML mapping",
                SpanOf(root, sourcePath)));
            return new LoadResult(configs.ToImmutable(), diagnostics.ToImmutable());
        }

        // CFG001 — unknown top-level keys (warning).
        YamlNode? emittersNode = null;
        var topKeys = new List<(string Key, YamlNode KeyNode, YamlNode Value)>();
        foreach (var kv in rootMap.Children)
        {
            if (kv.Key is YamlScalarNode keyScalar && keyScalar.Value is string keyText)
            {
                topKeys.Add((keyText, keyScalar, kv.Value));
            }
        }
        topKeys.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        foreach (var (keyText, keyNode, value) in topKeys)
        {
            if (string.Equals(keyText, "emitters", StringComparison.Ordinal))
            {
                emittersNode = value;
                continue;
            }
            if (!KnownTopLevelKeys.Contains(keyText))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    RuleIds.Cfg001,
                    "unknown top-level key '" + keyText + "' in .gravity.config",
                    SpanOf(keyNode, sourcePath)));
            }
        }

        if (emittersNode is null)
        {
            return new LoadResult(configs.ToImmutable(), diagnostics.ToImmutable());
        }
        if (emittersNode is not YamlMappingNode emittersMap)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Cfg002,
                "'emitters' must be a mapping of <name> -> <config block>",
                SpanOf(emittersNode, sourcePath)));
            return new LoadResult(configs.ToImmutable(), diagnostics.ToImmutable());
        }

        // Index emitters by TargetName, ordinal.
        var emittersByName = new Dictionary<string, IEmitter>(StringComparer.Ordinal);
        foreach (var e in registry.Emitters)
        {
            emittersByName[e.TargetName] = e;
        }

        // Walk each emitter block in ordinal key order so diagnostics are stable.
        var emitterEntries = new List<(string Name, YamlNode KeyNode, YamlNode Block)>();
        foreach (var kv in emittersMap.Children)
        {
            if (kv.Key is YamlScalarNode keyScalar && keyScalar.Value is string keyText)
            {
                emitterEntries.Add((keyText, keyScalar, kv.Value));
            }
        }
        emitterEntries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var (name, keyNode, block) in emitterEntries)
        {
            if (!emittersByName.TryGetValue(name, out var emitter))
            {
                // Unknown emitter: surface as CFG001 warning so a user with a stale
                // config gets a hint without aborting the run.
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    RuleIds.Cfg001,
                    "configuration for emitter '" + name + "' has no registered target; ignoring",
                    SpanOf(keyNode, sourcePath)));
                continue;
            }
            if (block is not YamlMappingNode blockMap)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Cfg002,
                    "emitter '" + name + "' block must be a mapping",
                    SpanOf(block, sourcePath)));
                continue;
            }
            var built = BuildOne(name, blockMap, emitter.ConfigurationSchema, sourcePath, diagnostics);
            if (built is not null)
            {
                configs[name] = built;
            }
        }

        return new LoadResult(configs.ToImmutable(), diagnostics.ToImmutable());
    }

    private static EmitterConfig? BuildOne(
        string target,
        YamlMappingNode block,
        EmitterConfigSchema schema,
        string sourcePath,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var schemaByName = new Dictionary<string, ConfigKey>(StringComparer.Ordinal);
        foreach (var k in schema.Keys) schemaByName[k.Name] = k;

        // 'enabled' (bool, optional, default true) and 'output' (string, required) are
        // built-in keys handled by the host. Emitter schemas declare their own keys.
        bool enabled = true;
        string? output = null;
        var values = ImmutableSortedDictionary.CreateBuilder<string, object>(StringComparer.Ordinal);

        // Collect entries in ordinal key order.
        var entries = new List<(string Key, YamlNode KeyNode, YamlNode Value)>();
        foreach (var kv in block.Children)
        {
            if (kv.Key is YamlScalarNode keyScalar && keyScalar.Value is string keyText)
            {
                entries.Add((keyText, keyScalar, kv.Value));
            }
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        foreach (var (key, keyNode, value) in entries)
        {
            switch (key)
            {
                case "enabled":
                {
                    if (!TryReadBool(value, out var b))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            RuleIds.Cfg002,
                            "emitter '" + target + "' key 'enabled' must be a boolean",
                            SpanOf(value, sourcePath)));
                        continue;
                    }
                    enabled = b;
                    continue;
                }
                case "output":
                {
                    if (!TryReadString(value, out var s))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            RuleIds.Cfg002,
                            "emitter '" + target + "' key 'output' must be a string",
                            SpanOf(value, sourcePath)));
                        continue;
                    }
                    output = s;
                    values[key] = s;
                    continue;
                }
            }

            if (!schemaByName.TryGetValue(key, out var ck))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    RuleIds.Cfg001,
                    "emitter '" + target + "' has unknown config key '" + key + "'",
                    SpanOf(keyNode, sourcePath)));
                continue;
            }
            if (!TryCoerce(ck, value, out var coerced))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Cfg002,
                    "emitter '" + target + "' key '" + key + "' must be a " + KindName(ck.Kind),
                    SpanOf(value, sourcePath)));
                continue;
            }
            values[key] = coerced!;
        }

        // Apply defaults & enforce required.
        bool missingRequired = false;
        foreach (var ck in schema.Keys)
        {
            if (values.ContainsKey(ck.Name)) continue;
            if (ck.Required)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Cfg003,
                    "emitter '" + target + "' is missing required config key '" + ck.Name + "'",
                    SpanOf(block, sourcePath)));
                missingRequired = true;
                continue;
            }
            if (ck.Default is not null)
            {
                values[ck.Name] = ck.Default;
            }
        }

        if (output is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Cfg003,
                "emitter '" + target + "' is missing required config key 'output'",
                SpanOf(block, sourcePath)));
            missingRequired = true;
        }

        if (missingRequired) return null;

        return new EmitterConfig(target, enabled, output!, values.ToImmutable());
    }

    private static bool TryCoerce(ConfigKey ck, YamlNode node, out object? value)
    {
        switch (ck.Kind)
        {
            case ConfigValueKind.String:
                if (TryReadString(node, out var s)) { value = s; return true; }
                break;
            case ConfigValueKind.Int:
                if (TryReadInt(node, out var i)) { value = i; return true; }
                break;
            case ConfigValueKind.Bool:
                if (TryReadBool(node, out var b)) { value = b; return true; }
                break;
        }
        value = null;
        return false;
    }

    private static bool TryReadString(YamlNode node, out string value)
    {
        if (node is YamlScalarNode s && s.Value is not null)
        {
            // Reject anything that parses cleanly as a boolean or integer to keep
            // typing strict — YAML 1.1 happily reads "true" as both. This mirrors
            // YAML 1.2 strictness expectations.
            value = s.Value;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static bool TryReadInt(YamlNode node, out long value)
    {
        if (node is YamlScalarNode s && s.Value is not null
            && long.TryParse(s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            value = n;
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryReadBool(YamlNode node, out bool value)
    {
        if (node is YamlScalarNode s && s.Value is not null)
        {
            if (string.Equals(s.Value, "true", StringComparison.Ordinal)) { value = true; return true; }
            if (string.Equals(s.Value, "false", StringComparison.Ordinal)) { value = false; return true; }
        }
        value = false;
        return false;
    }

    private static string KindName(ConfigValueKind kind) => kind switch
    {
        ConfigValueKind.String => "string",
        ConfigValueKind.Int => "integer",
        ConfigValueKind.Bool => "boolean",
        _ => "value"
    };

    private static SourceSpan SpanOf(YamlNode node, string sourcePath)
    {
        var start = node.Start;
        int line = start.Line > 0 ? (int)start.Line : 1;
        int col = start.Column > 0 ? (int)start.Column : 1;
        return new SourceSpan(sourcePath, line, col, 0);
    }
}
