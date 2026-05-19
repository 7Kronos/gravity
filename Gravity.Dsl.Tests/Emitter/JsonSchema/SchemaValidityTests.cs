using System;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Gravity.Dsl.Emitter;
using Gravity.Dsl.Emitter.JsonSchema;
using Gravity.Dsl.Tests.Helpers;
using Xunit;
using JS = Json.Schema;

namespace Gravity.Dsl.Tests.Emitter.JsonSchema;

/// <summary>
/// AC-4.4 — validate every emitted file against the Draft-07 metaschema using
/// JsonSchema.Net. Test-only dependency; the emitter itself depends on the BCL
/// <c>System.Text.Json</c> only (FR-363).
/// </summary>
public sealed class SchemaValidityTests
{
    [Fact]
    [Trait("Category", "Slow")]
    public void EveryEmittedFile_IsValidDraft07()
    {
        var model = SamplesLoader.LoadRegistry();
        var sink = new BufferedEmitterOutput();
        var cfg = new EmitterConfig(
            TargetName: "json-schema", Enabled: true, Output: "json-schema",
            Values: ImmutableSortedDictionary<string, object>.Empty
                .Add("output", "json-schema"));
        var result = new JsonSchemaEmitter().Emit(model, cfg, sink);
        result.Diagnostics.Should().BeEmpty();

        var snap = sink.Snapshot();
        snap.Should().HaveCount(20, because: "AC-4.3: 3 entities + 15 value types + 2 enums = 20");

        // Draft-07 metaschema is built into JsonSchema.Net's MetaSchemas registry.
        // We validate each emitted document against MetaSchemas.Draft7.
        var failures = new System.Collections.Generic.List<(string path, string error)>();
        foreach (var kv in snap)
        {
            JS.JsonSchema parsed;
            try
            {
                parsed = JS.JsonSchema.FromText(kv.Value);
            }
            catch (Exception ex)
            {
                failures.Add((kv.Key, "FromText threw: " + ex.GetType().Name + ": " + ex.Message));
                continue;
            }
            // Re-validate the schema's own JSON text against the Draft-07 metaschema.
            // JsonSchema.Net 7.x Evaluate takes a JsonNode, not a JsonElement.
            var node = System.Text.Json.Nodes.JsonNode.Parse(kv.Value);
            var eval = JS.MetaSchemas.Draft7.Evaluate(node, new JS.EvaluationOptions
            {
                OutputFormat = JS.OutputFormat.List,
            });
            if (!eval.IsValid)
            {
                var details = string.Join("; ", eval.Details
                    .Where(d => d.HasErrors)
                    .SelectMany(d => d.Errors!.Select(e => d.EvaluationPath + ": " + e.Key + "=" + e.Value)));
                failures.Add((kv.Key, details));
            }
        }
        failures.Should().BeEmpty(
            because: "AC-4.4: every emitted file must be a valid Draft-07 schema. Failures: "
                + string.Join("\n", failures.Select(f => f.path + " -> " + f.error)));
    }
}
