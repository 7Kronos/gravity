using System.Globalization;
using System.Text;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Emitter.Sample.Outline.Render;

/// <summary>
/// Markdown renderer for one <see cref="EntityDecl"/>. Emits six fixed sections —
/// Identity / Relations / Properties / Lifecycle / Events / Commands — in declaration
/// order under each. Output is byte-deterministic per FR-222: LF line endings,
/// invariant-culture integer rendering, no timestamps, no machine-local paths.
/// See specs/003-phase-9-build-integration/spec.md FR-221 / AC-9.6.
/// </summary>
internal static class EntityOutlineRenderer
{
    private const string Lf = "\n";

    /// <summary>Render <paramref name="entity"/> at <paramref name="version"/> as a Markdown document.</summary>
    public static string Render(EntityDecl entity, int version)
    {
        var sb = new StringBuilder();
        // H1: entity name @ version (FR-221).
        sb.Append("# ").Append(entity.Name).Append('@')
          .Append(version.ToString(CultureInfo.InvariantCulture)).Append(Lf);
        sb.Append(Lf);

        AppendIdentity(sb, entity);
        AppendRelations(sb, entity);
        AppendProperties(sb, entity);
        AppendLifecycle(sb, entity);
        AppendEvents(sb, entity);
        AppendCommands(sb, entity);

        return sb.ToString();
    }

    private static void AppendIdentity(StringBuilder sb, EntityDecl entity)
    {
        sb.Append("## Identity").Append(Lf).Append(Lf);
        sb.Append("| name | type |").Append(Lf);
        sb.Append("| --- | --- |").Append(Lf);
        sb.Append("| ").Append(entity.Identity.FieldName)
          .Append(" | ").Append(TypeRenderer.Render(entity.Identity.Type))
          .Append(" |").Append(Lf);
        sb.Append(Lf);
    }

    private static void AppendRelations(StringBuilder sb, EntityDecl entity)
    {
        sb.Append("## Relations").Append(Lf).Append(Lf);
        if (entity.Relations.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return;
        }
        sb.Append("| name | target | cardinality | optionality | semantic |").Append(Lf);
        sb.Append("| --- | --- | --- | --- | --- |").Append(Lf);
        foreach (var r in entity.Relations)
        {
            sb.Append("| ").Append(r.Name)
              .Append(" | ").Append(r.TargetEntity)
              .Append(" | ").Append(r.Cardinality == Cardinality.Many ? "many" : "one")
              .Append(" | ").Append(r.IsOptional ? "optional" : "required")
              .Append(" | ").Append(string.IsNullOrEmpty(r.Semantic) ? "_" : r.Semantic)
              .Append(" |").Append(Lf);
        }
        sb.Append(Lf);
    }

    private static void AppendProperties(StringBuilder sb, EntityDecl entity)
    {
        sb.Append("## Properties").Append(Lf).Append(Lf);
        if (entity.Properties.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return;
        }
        sb.Append("| name | type |").Append(Lf);
        sb.Append("| --- | --- |").Append(Lf);
        foreach (var p in entity.Properties)
        {
            sb.Append("| ").Append(p.Name)
              .Append(" | ").Append(TypeRenderer.Render(p.Type))
              .Append(" |").Append(Lf);
        }
        sb.Append(Lf);
    }

    private static void AppendLifecycle(StringBuilder sb, EntityDecl entity)
    {
        sb.Append("## Lifecycle").Append(Lf).Append(Lf);

        sb.Append("### States").Append(Lf).Append(Lf);
        if (entity.Lifecycle.States.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
        }
        else
        {
            foreach (var state in entity.Lifecycle.States)
            {
                sb.Append("- ").Append(state).Append(Lf);
            }
            sb.Append(Lf);
        }

        sb.Append("### Transitions").Append(Lf).Append(Lf);
        if (entity.Lifecycle.Transitions.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return;
        }
        sb.Append("| from | to | on |").Append(Lf);
        sb.Append("| --- | --- | --- |").Append(Lf);
        foreach (var t in entity.Lifecycle.Transitions)
        {
            sb.Append("| ").Append(t.From)
              .Append(" | ").Append(t.To)
              .Append(" | ").Append(t.OnEvent)
              .Append(" |").Append(Lf);
        }
        sb.Append(Lf);
    }

    private static void AppendEvents(StringBuilder sb, EntityDecl entity)
    {
        sb.Append("## Events").Append(Lf).Append(Lf);
        if (entity.Events.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return;
        }
        foreach (var evt in entity.Events)
        {
            sb.Append("### ").Append(evt.Name).Append(Lf).Append(Lf);
            if (evt.Payload.Length == 0)
            {
                sb.Append("_(no payload)_").Append(Lf).Append(Lf);
                continue;
            }
            sb.Append("| field | type |").Append(Lf);
            sb.Append("| --- | --- |").Append(Lf);
            foreach (var f in evt.Payload)
            {
                sb.Append("| ").Append(f.Name)
                  .Append(" | ").Append(TypeRenderer.Render(f.Type))
                  .Append(" |").Append(Lf);
            }
            sb.Append(Lf);
        }
    }

    private static void AppendCommands(StringBuilder sb, EntityDecl entity)
    {
        sb.Append("## Commands").Append(Lf).Append(Lf);
        if (entity.Commands.Length == 0)
        {
            sb.Append("_(none)_").Append(Lf).Append(Lf);
            return;
        }
        foreach (var cmd in entity.Commands)
        {
            sb.Append("### ").Append(cmd.Name).Append(Lf).Append(Lf);
            if (cmd.Arguments.Length == 0)
            {
                sb.Append("_(no arguments)_").Append(Lf).Append(Lf);
            }
            else
            {
                sb.Append("| arg | type |").Append(Lf);
                sb.Append("| --- | --- |").Append(Lf);
                foreach (var a in cmd.Arguments)
                {
                    sb.Append("| ").Append(a.Name)
                      .Append(" | ").Append(TypeRenderer.Render(a.Type))
                      .Append(" |").Append(Lf);
                }
                sb.Append(Lf);
            }
            sb.Append("returns: ").Append(cmd.ReturnsType).Append(Lf);
            sb.Append("with side_effect: ").Append(cmd.SideEffectEvent).Append(Lf);
            sb.Append(Lf);
        }
    }
}
