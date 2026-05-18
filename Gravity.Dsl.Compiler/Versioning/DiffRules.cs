using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gravity.Dsl.Ast;
using Gravity.Dsl.Compiler.Resolution;
using Gravity.Dsl.Compiler.Validation;

namespace Gravity.Dsl.Compiler.Versioning;

/// <summary>
/// Phase 8 (P8c) per-rule diff bodies invoked by <see cref="VersionDiff"/>.
/// Per-pair rules (VAL020..VAL026) inspect a chained <c>(Vprev, Vnext)</c>
/// pair of <see cref="TopLevelDecl"/>s; per-decl rules (VAL027, VAL028, VAL029)
/// walk the full resolved model; the window check (VAL030) consumes the
/// injected <see cref="DateOnly"/>.
/// </summary>
internal static class DiffRules
{
    // Container labels (FR-130 / FR-136).
    private const string ContainerEntityProperty = "entity-property";
    private const string ContainerValueTypeField = "value-type-field";
    private const string ContainerEventPayload = "event-payload";

    // Spec FR-130 lists 'command-argument' as a VAL020 container, but FR-136 routes
    // command-arg-removal through VAL026 sub-cause "argument removed". The
    // implementation honors FR-136 to avoid double-reporting.

    // ------- VAL020: field removed (FR-130) -------

    /// <summary>
    /// Builds the canonical FR-130 VAL020 message body, with the FR-150 hint
    /// appended uniformly across containers (entity-property, value-type-field,
    /// event-payload). The hint names the prior version so a downstream reader
    /// can find the field declaration without re-walking the source.
    /// </summary>
    private static string BuildVal020Message(string container, string qualifiedName,
        string fqn, int vprev, int vnext)
    {
        return container + "." + qualifiedName + " was removed in " + fqn + "@"
            + vnext.ToString(CultureInfo.InvariantCulture)
            + "; field removal is a breaking change"
            + "; the prior version '" + fqn + "@" + vprev.ToString(CultureInfo.InvariantCulture)
            + "' declared this field — keep it, mark it '?', or add a 'deprecates' chain and a future"
            + " '@deprecated' field annotation when that lands";
    }

    public static void ApplyVal020(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is EntityDecl ep && next is EntityDecl en)
        {
            // entity-property
            foreach (var removed in NamesIn(ep.Properties.Select(p => p.Name))
                .Except(NamesIn(en.Properties.Select(p => p.Name)), StringComparer.Ordinal)
                .OrderBy(n => Array.IndexOf(ep.Properties.Select(p => p.Name).ToArray(), n)))
            {
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val020,
                    BuildVal020Message(ContainerEntityProperty, removed, fqn, ep.Version, en.Version),
                    en.Span), fqn, en.Version);
            }

            // event-payload — only when the event itself survives; event removal is VAL024.
            var nextEventByName = en.Events.ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (var prevEvt in ep.Events)
            {
                if (!nextEventByName.TryGetValue(prevEvt.Name, out var nextEvt)) continue;
                var prevNames = prevEvt.Payload.Select(f => f.Name).ToArray();
                var nextNames = new HashSet<string>(nextEvt.Payload.Select(f => f.Name), StringComparer.Ordinal);
                foreach (var pn in prevNames)
                {
                    if (nextNames.Contains(pn)) continue;
                    sink.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        RuleIds.Val020,
                        BuildVal020Message(ContainerEventPayload, prevEvt.Name + "." + pn,
                            fqn, ep.Version, en.Version),
                        nextEvt.Span), fqn, en.Version);
                }
            }
        }
        else if (prev is ValueTypeDecl vp && next is ValueTypeDecl vn)
        {
            var prevNames = vp.Fields.Select(f => f.Name).ToArray();
            var nextNames = new HashSet<string>(vn.Fields.Select(f => f.Name), StringComparer.Ordinal);
            foreach (var pn in prevNames)
            {
                if (nextNames.Contains(pn)) continue;
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val020,
                    BuildVal020Message(ContainerValueTypeField, pn, fqn, vp.Version, vn.Version),
                    vn.Span), fqn, vn.Version);
            }
        }
    }

    // ------- VAL021: type narrowed (FR-131) -------

    public static void ApplyVal021(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is EntityDecl ep && next is EntityDecl en)
        {
            // entity-property
            DiffSurvivingFields(
                ep.Properties.Select(p => (p.Name, p.Type, p.Span)),
                en.Properties.Select(p => (p.Name, p.Type, p.Span)),
                (pf, nf) => Report(ContainerEntityProperty, pf.Name, pf.Type, nf.Type, nf.Span, en.Version));

            // event-payload on surviving events.
            var nextEventByName = en.Events.ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (var prevEvt in ep.Events)
            {
                if (!nextEventByName.TryGetValue(prevEvt.Name, out var nextEvt)) continue;
                DiffSurvivingFields(
                    prevEvt.Payload.Select(f => (f.Name, f.Type, f.Span)),
                    nextEvt.Payload.Select(f => (f.Name, f.Type, f.Span)),
                    (pf, nf) => Report(
                        ContainerEventPayload + "." + prevEvt.Name,
                        pf.Name, pf.Type, nf.Type, nf.Span, en.Version));
            }
        }
        else if (prev is ValueTypeDecl vp && next is ValueTypeDecl vn)
        {
            DiffSurvivingFields(
                vp.Fields.Select(f => (f.Name, f.Type, f.Span)),
                vn.Fields.Select(f => (f.Name, f.Type, f.Span)),
                (pf, nf) => Report(ContainerValueTypeField, pf.Name, pf.Type, nf.Type, nf.Span, vn.Version));
        }

        void Report(string container, string fieldName, TypeRef prevType, TypeRef nextType,
            SourceSpan span, int vnext)
        {
            if (!Narrowing.IsNarrowing(prevType, nextType)) return;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val021,
                container + "." + fieldName + ": type narrowed from "
                    + TypeRefRenderer.Render(prevType) + " to " + TypeRefRenderer.Render(nextType)
                    + " in " + fqn + "@" + vnext.ToString(CultureInfo.InvariantCulture),
                span), fqn, vnext);
        }
    }

    // ------- VAL022: lifecycle state removed (FR-132) -------

    public static void ApplyVal022(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is not EntityDecl ep || next is not EntityDecl en) return;
        var nextStates = new HashSet<string>(en.Lifecycle.States, StringComparer.Ordinal);
        foreach (var s in ep.Lifecycle.States)
        {
            if (nextStates.Contains(s)) continue;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val022,
                "lifecycle state '" + s + "' removed from " + fqn + "@"
                    + en.Version.ToString(CultureInfo.InvariantCulture)
                    + "; state removal is a breaking change",
                en.Lifecycle.Span), fqn, en.Version);
        }
    }

    // ------- VAL023: command removed (FR-133) -------

    public static void ApplyVal023(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is not EntityDecl ep || next is not EntityDecl en) return;
        var nextCmds = new HashSet<string>(en.Commands.Select(c => c.Name), StringComparer.Ordinal);
        foreach (var cmd in ep.Commands)
        {
            if (nextCmds.Contains(cmd.Name)) continue;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val023,
                "command '" + cmd.Name + "' removed from " + fqn + "@"
                    + en.Version.ToString(CultureInfo.InvariantCulture)
                    + "; command removal is a breaking change",
                en.Span), fqn, en.Version);
        }
    }

    // ------- VAL024: event removed (FR-134) -------

    public static void ApplyVal024(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is not EntityDecl ep || next is not EntityDecl en) return;
        var nextEvents = new HashSet<string>(en.Events.Select(e => e.Name), StringComparer.Ordinal);
        foreach (var evt in ep.Events)
        {
            if (nextEvents.Contains(evt.Name)) continue;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val024,
                "event '" + evt.Name + "' removed from " + fqn + "@"
                    + en.Version.ToString(CultureInfo.InvariantCulture)
                    + "; event removal is a breaking change",
                en.Span), fqn, en.Version);
        }
    }

    // ------- VAL025: transition removed (warning, FR-135) -------

    public static void ApplyVal025(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is not EntityDecl ep || next is not EntityDecl en) return;
        var nextTriples = new HashSet<(string From, string To, string OnEvent)>();
        foreach (var t in en.Lifecycle.Transitions)
        {
            nextTriples.Add((t.From, t.To, t.OnEvent));
        }
        foreach (var t in ep.Lifecycle.Transitions)
        {
            if (nextTriples.Contains((t.From, t.To, t.OnEvent))) continue;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                RuleIds.Val025,
                "transition '" + t.From + " -> " + t.To + " on " + t.OnEvent
                    + "' removed from " + fqn + "@"
                    + en.Version.ToString(CultureInfo.InvariantCulture),
                en.Lifecycle.Span), fqn, en.Version);
        }
    }

    // ------- VAL026: command argument breaking change (FR-136) -------

    public static void ApplyVal026(
        TopLevelDecl prev, TopLevelDecl next, string fqn, DiagnosticSink sink)
    {
        if (prev is not EntityDecl ep || next is not EntityDecl en) return;
        var nextCmdByName = en.Commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (var prevCmd in ep.Commands)
        {
            if (!nextCmdByName.TryGetValue(prevCmd.Name, out var nextCmd)) continue;

            var prevArgs = prevCmd.Arguments;
            var nextArgs = nextCmd.Arguments;
            var prevByName = prevArgs.ToDictionary(a => a.Name, StringComparer.Ordinal);
            var nextByName = nextArgs.ToDictionary(a => a.Name, StringComparer.Ordinal);

            // (a) argument removed (also covers the removal half of a rename).
            foreach (var pa in prevArgs)
            {
                if (nextByName.ContainsKey(pa.Name)) continue;
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val026,
                    "command '" + prevCmd.Name + "' on " + fqn + "@"
                        + en.Version.ToString(CultureInfo.InvariantCulture)
                        + ": argument removed: '" + pa.Name + "'",
                    nextCmd.Span), fqn, en.Version);
            }

            // (c) argument type narrowed (same-named survivors only).
            foreach (var pa in prevArgs)
            {
                if (!nextByName.TryGetValue(pa.Name, out var na)) continue;
                if (!Narrowing.IsNarrowing(pa.Type, na.Type)) continue;
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val026,
                    "command '" + prevCmd.Name + "' on " + fqn + "@"
                        + en.Version.ToString(CultureInfo.InvariantCulture)
                        + ": argument '" + pa.Name + "': argument type narrowed from "
                        + TypeRefRenderer.Render(pa.Type) + " to " + TypeRefRenderer.Render(na.Type),
                    na.Span), fqn, en.Version);
            }

            // (d) new required argument added (also covers the addition half of a
            // rename when the new arg is required).
            foreach (var na in nextArgs)
            {
                if (prevByName.ContainsKey(na.Name)) continue;
                if (IsOptional(na.Type)) continue;
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val026,
                    "command '" + prevCmd.Name + "' on " + fqn + "@"
                        + en.Version.ToString(CultureInfo.InvariantCulture)
                        + ": required argument added: '" + na.Name + "'",
                    na.Span), fqn, en.Version);
            }
        }
    }

    // ------- VAL027: deprecates chain broken (skipped link, FR-137) -------

    public static void ApplyVal027(ResolvedModel model, DiagnosticSink sink)
    {
        foreach (var kv in model.VersionIndex)
        {
            var fqn = kv.Key;
            var versions = kv.Value;
            if (versions.Length < 2) continue;
            for (int i = 1; i < versions.Length; i++)
            {
                int prev = versions[i - 1];
                int next = versions[i];
                if (!model.Declarations.TryGetValue(new DeclKey(fqn, next), out var nextDecl)) continue;
                if (nextDecl is not EntityDecl ent || ent.Deprecates is not { } dep) continue;
                // VAL027 only fires when the chain points further back than the
                // immediately-preceding version. Pointing to a non-existent version
                // is VAL028; self/forward is VAL029. Pointing to the right prev is OK.
                if (dep.Version == prev) continue;
                if (dep.Version >= ent.Version) continue;                    // VAL029 territory
                bool depIsDeclaredEarlier = false;
                for (int j = 0; j < i; j++)
                {
                    if (versions[j] == dep.Version) { depIsDeclaredEarlier = true; break; }
                }
                if (!depIsDeclaredEarlier) continue;                          // VAL028 territory
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val027,
                    "deprecates chain broken: " + fqn + "@" + prev.ToString(CultureInfo.InvariantCulture)
                        + " coexists with " + fqn + "@" + next.ToString(CultureInfo.InvariantCulture)
                        + " but is not named in its deprecates clause",
                    dep.Span), fqn, next);
            }
        }
    }

    // ------- VAL028: deprecates names non-existent version (FR-124) -------

    public static void ApplyVal028(ResolvedModel model, DiagnosticSink sink)
    {
        foreach (var kv in model.Declarations)
        {
            if (kv.Value is not EntityDecl ent || ent.Deprecates is not { } dep) continue;
            var fqn = kv.Key.Fqn;
            if (!model.VersionIndex.TryGetValue(fqn, out var versions)) continue;
            bool found = false;
            foreach (var v in versions)
            {
                if (v == dep.Version) { found = true; break; }
            }
            if (found) continue;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val028,
                "deprecates version " + dep.Version.ToString(CultureInfo.InvariantCulture)
                    + " references no declared version of " + fqn,
                dep.Span), fqn, ent.Version);
        }
    }

    // ------- VAL029: deprecates self / forward reference (FR-125) -------

    public static void ApplyVal029(ResolvedModel model, DiagnosticSink sink)
    {
        foreach (var kv in model.Declarations)
        {
            if (kv.Value is not EntityDecl ent || ent.Deprecates is not { } dep) continue;
            if (dep.Version < ent.Version) continue;
            var fqn = kv.Key.Fqn;
            sink.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                RuleIds.Val029,
                "entity " + fqn + "@" + ent.Version.ToString(CultureInfo.InvariantCulture)
                    + " may not deprecate version " + dep.Version.ToString(CultureInfo.InvariantCulture)
                    + "; " + dep.Version.ToString(CultureInfo.InvariantCulture)
                    + " must be strictly less than " + ent.Version.ToString(CultureInfo.InvariantCulture),
                dep.Span), fqn, ent.Version);
        }
    }

    // ------- VAL030: deprecation window expired (FR-138) -------

    public static void ApplyVal030(ResolvedModel model, DateOnly currentDate, DiagnosticSink sink)
    {
        foreach (var kv in model.Declarations)
        {
            if (kv.Value is not EntityDecl ent || ent.Deprecates is not { } dep) continue;
            // Phase 0–3 VAL009 already gates well-formedness; ParseExact is safe here,
            // and a well-formedness failure earlier in the pipeline means we never reach
            // this code with an invalid string.
            if (!DateOnly.TryParseExact(
                    dep.UntilIso8601, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
            {
                continue;
            }
            if (until < currentDate)
            {
                var fqn = kv.Key.Fqn;
                sink.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    RuleIds.Val030,
                    "deprecation window for " + fqn + "@" + dep.Version.ToString(CultureInfo.InvariantCulture)
                        + " expired on " + until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        + "; remove the deprecated version or extend the window",
                    dep.Span), fqn, ent.Version);
            }
        }
    }

    // ------- helpers -------

    private static IEnumerable<string> NamesIn(IEnumerable<string> names) => names;

    private static bool IsOptional(TypeRef t) => t switch
    {
        PrimitiveTypeRef p => p.IsOptional,
        NamedTypeRef n => n.IsOptional,
        _ => false
    };

    /// <summary>
    /// Walks every <paramref name="prev"/> field, and when a same-named field exists in
    /// <paramref name="next"/>, invokes <paramref name="onSurvivor"/> with the pair.
    /// Order follows <paramref name="prev"/> declaration order (deterministic, FR-160-friendly).
    /// </summary>
    private static void DiffSurvivingFields(
        IEnumerable<(string Name, TypeRef Type, SourceSpan Span)> prev,
        IEnumerable<(string Name, TypeRef Type, SourceSpan Span)> next,
        Action<(string Name, TypeRef Type, SourceSpan Span), (string Name, TypeRef Type, SourceSpan Span)> onSurvivor)
    {
        var nextByName = new Dictionary<string, (string Name, TypeRef Type, SourceSpan Span)>(StringComparer.Ordinal);
        foreach (var f in next) nextByName[f.Name] = f;
        foreach (var pf in prev)
        {
            if (!nextByName.TryGetValue(pf.Name, out var nf)) continue;
            onSurvivor(pf, nf);
        }
    }
}
