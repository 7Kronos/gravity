using System;
using System.Collections.Generic;
using System.Globalization;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Versioning;

namespace Gravity.Dsl.Compiler.Validation;

/// <summary>
/// Runs semantic validation rules on a <see cref="ResolvedModel"/>. Rule set
/// implemented: Phase 0–3 (<c>VAL001..VAL006, VAL009, VAL010</c>) plus the Phase 8
/// breaking-change pass (<c>VAL020..VAL030</c>). <c>VAL007</c> (annotation
/// namespace claimed by two emitters) is enforced by the emitter host.
/// </summary>
public static class Validator
{
    /// <summary>
    /// Validate the resolved model. The compiler library never reads the clock;
    /// callers supply <paramref name="currentDate"/> (the CLI threads
    /// <c>--as-of</c> or the operator-defaulted UTC date).
    /// </summary>
    /// <param name="model">The resolved model.</param>
    /// <param name="claimedAnnotationNamespaces">The set of annotation namespaces
    /// claimed by registered emitters. Phase 1 callers pass a hard-coded set
    /// (e.g. <c>{ "csharp" }</c>); Phase 2 will populate this from the emitter host.</param>
    /// <param name="currentDate">The date to evaluate Phase 8 deprecation windows
    /// against (FR-140). Passing <c>default(DateOnly)</c> (i.e. <c>0001-01-01</c>)
    /// is the deterministic "always-in-window" choice used by Phase 0–3 callers
    /// that do not exercise the versioning surface.</param>
    public static IReadOnlyList<Diagnostic> Validate(
        ResolvedModel model,
        IReadOnlyCollection<string> claimedAnnotationNamespaces,
        DateOnly currentDate)
    {
        var diagnostics = new List<Diagnostic>();
        var claimed = new HashSet<string>(claimedAnnotationNamespaces, StringComparer.Ordinal);

        foreach (var kv in model.Declarations)
        {
            var decl = kv.Value;
            switch (decl)
            {
                case EntityDecl entity:
                    ValidateEntity(entity, claimed, diagnostics);
                    break;
                case ValueTypeDecl vt:
                    ValidateAnnotations(vt.Annotations, claimed, diagnostics);
                    break;
                case EnumDecl en:
                    ValidateAnnotations(en.Annotations, claimed, diagnostics);
                    break;
            }
            if (decl is EntityDecl ent && ent.Deprecates is { } dep)
            {
                ValidateDeprecatesDate(dep, diagnostics);
            }
        }

        // Phase 8 breaking-change pass (VAL020..VAL030). The pass returns its
        // diagnostics in FR-160 order; we append them after the Phase 0–3 block
        // to preserve existing Phase 0–3 ordering and goldens (plan.md §4(b)).
        var phase8 = VersionDiff.Run(model, currentDate);
        foreach (var d in phase8) diagnostics.Add(d);

        return diagnostics;
    }

    private static void ValidateEntity(EntityDecl entity, HashSet<string> claimed, List<Diagnostic> diagnostics)
    {
        // VAL005: identity type is not UUID -> warning.
        if (entity.Identity.Type is PrimitiveTypeRef p)
        {
            if (p.Kind != PrimitiveKind.Uuid)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    RuleIds.Val005,
                    "identity field '" + entity.Identity.FieldName + "' on entity '" + entity.Name
                        + "' is not UUID; UUID is recommended",
                    entity.Identity.Span));
            }
        }
        else
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                RuleIds.Val005,
                "identity field '" + entity.Identity.FieldName + "' on entity '" + entity.Name
                    + "' is not UUID; UUID is recommended",
                entity.Identity.Span));
        }

        // VAL010: relation with IsOptional=true AND Cardinality.Many.
        foreach (var rel in entity.Relations)
        {
            if (rel.IsOptional && rel.Cardinality == Cardinality.Many)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val010,
                    "relation '" + rel.Name + "' on entity '" + entity.Name
                        + "' may not combine '?' with 'cardinality many'; an empty 'many' relation uses an empty collection",
                    rel.Span));
            }
            ValidateAnnotations(rel.Annotations, claimed, diagnostics);
        }

        // Property annotations.
        foreach (var prop in entity.Properties)
        {
            ValidateAnnotations(prop.Annotations, claimed, diagnostics);
        }

        // Entity-level annotations.
        ValidateAnnotations(entity.Annotations, claimed, diagnostics);

        // Lifecycle rules: VAL001, VAL002, VAL004.
        var stateSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var st in entity.Lifecycle.States) stateSet.Add(st);
        var eventSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evt in entity.Events) eventSet.Add(evt.Name);

        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tr in entity.Lifecycle.Transitions)
        {
            if (!stateSet.Contains(tr.From))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val001,
                    "transition references state '" + tr.From + "' which is not declared in 'states' for entity '"
                        + entity.Name + "'",
                    tr.Span));
            }
            if (!stateSet.Contains(tr.To))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val001,
                    "transition references state '" + tr.To + "' which is not declared in 'states' for entity '"
                        + entity.Name + "'",
                    tr.Span));
            }
            if (!eventSet.Contains(tr.OnEvent))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val002,
                    "transition's 'on' event '" + tr.OnEvent + "' is not declared in 'events' for entity '"
                        + entity.Name + "'",
                    tr.Span));
            }
            incoming.Add(tr.To);
        }

        // VAL004: declared state with no incoming transition (excluding the first state).
        for (int i = 1; i < entity.Lifecycle.States.Length; i++)
        {
            var state = entity.Lifecycle.States[i];
            if (!incoming.Contains(state))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    RuleIds.Val004,
                    "state '" + state + "' on entity '" + entity.Name + "' has no incoming transition",
                    entity.Lifecycle.Span));
            }
        }

        // VAL003: command side_effect event not in events {}.
        foreach (var cmd in entity.Commands)
        {
            if (!eventSet.Contains(cmd.SideEffectEvent))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val003,
                    "command '" + cmd.Name + "' on entity '" + entity.Name
                        + "' has side_effect event '" + cmd.SideEffectEvent + "' which is not declared in 'events'",
                    cmd.Span));
            }
            ValidateAnnotations(cmd.Annotations, claimed, diagnostics);
        }

        foreach (var evt in entity.Events)
        {
            ValidateAnnotations(evt.Annotations, claimed, diagnostics);
        }
    }

    private static void ValidateAnnotations(
        System.Collections.Immutable.ImmutableArray<AnnotationDecl> annotations,
        HashSet<string> claimed,
        List<Diagnostic> diagnostics)
    {
        foreach (var a in annotations)
        {
            if (!claimed.Contains(a.Namespace))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val006,
                    "annotation namespace '" + a.Namespace + "' is not claimed by any registered emitter",
                    a.Span));
            }
        }
    }

    private static void ValidateDeprecatesDate(DeprecatesClause dep, List<Diagnostic> diagnostics)
    {
        var s = dep.UntilIso8601;
        bool wellFormed = s.Length == 10
            && s[4] == '-' && s[7] == '-'
            && IsAsciiDigit(s[0]) && IsAsciiDigit(s[1]) && IsAsciiDigit(s[2]) && IsAsciiDigit(s[3])
            && IsAsciiDigit(s[5]) && IsAsciiDigit(s[6])
            && IsAsciiDigit(s[8]) && IsAsciiDigit(s[9]);
        if (!wellFormed)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val009,
                "deprecates date '" + s + "' must match the format YYYY-MM-DD",
                dep.Span));
            return;
        }
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val009,
                "deprecates date '" + s + "' is not a valid calendar date",
                dep.Span));
        }
    }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}
