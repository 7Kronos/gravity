using System.Collections.Immutable;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.JsonSchema;

/// <summary>
/// Typed projection of the host-validated <see cref="EmitterConfig"/> for the
/// JSON Schema emitter. Mirrors the shape of
/// <c>OutlineEmitterConfig.From</c> so a community author copy-pasting this
/// template recognises the idiom.
/// </summary>
internal sealed class JsonSchemaEmitterConfig
{
    /// <summary>The relative output directory under the host's <c>outputRoot</c>.</summary>
    public string Output { get; }

    /// <summary>The bundle layout strategy. Only <c>"per-entity"</c> is legal in v1.</summary>
    public string BundleStrategy { get; }

    private JsonSchemaEmitterConfig(string output, string bundleStrategy)
    {
        Output = output;
        BundleStrategy = bundleStrategy;
    }

    /// <summary>
    /// Project + pre-flight. Returns null and appends <c>JS002</c> to
    /// <paramref name="diags"/> when <c>bundle_strategy</c> is set to a value
    /// other than <c>"per-entity"</c> (FR-302, FR-364).
    /// </summary>
    public static JsonSchemaEmitterConfig? From(EmitterConfig config, ImmutableArray<Diagnostic>.Builder diags)
    {
        // ConfigLoader has already validated the schema (required + string-typed);
        // GetString throws on misuse, which is intentional — the host should not
        // call Emit with a malformed config.
        string output = config.GetString(JsonSchemaEmitter.ConfigKeyOutput);
        string strategy = config.Values.TryGetValue(JsonSchemaEmitter.ConfigKeyBundleStrategy, out var raw) && raw is string s
            ? s
            : JsonSchemaEmitter.DefaultBundleStrategy;
        if (!string.Equals(strategy, JsonSchemaEmitter.DefaultBundleStrategy, System.StringComparison.Ordinal))
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                JsonRuleIds.Js002,
                "bundle_strategy '" + strategy + "' is not recognised; the only legal value in v1 is 'per-entity'",
                new SourceSpan(JsonSchemaEmitter.TargetNameValue, 1, 1, 0)));
            return null;
        }
        return new JsonSchemaEmitterConfig(output, strategy);
    }
}
