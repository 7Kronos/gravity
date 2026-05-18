using System.Collections.Generic;
using System.Collections.Immutable;
using Gravity.Dsl.Ast;

namespace Gravity.Dsl.Tests.Helpers;

/// <summary>
/// Compares two AST trees ignoring <see cref="SourceSpan"/> values.
/// Used by round-trip tests where the second parse necessarily produces
/// different line/column information.
/// </summary>
internal static class SpanIgnoringEquality
{
    public static bool Equal(SourceFile? a, SourceFile? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        // Ignore Path because round-trip targets a different (or no) path.
        if (!Equal(a.Namespace, b.Namespace)) return false;
        if (!SequenceEqual(a.Imports, b.Imports, Equal)) return false;
        if (!SequenceEqual(a.Declarations, b.Declarations, Equal)) return false;
        return true;
    }

    private static bool Equal(NamespaceDecl? a, NamespaceDecl? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Name == b.Name;
    }

    private static bool Equal(ImportDecl a, ImportDecl b) =>
        a.RelativePath == b.RelativePath;

    private static bool Equal(TopLevelDecl a, TopLevelDecl b)
    {
        if (a is EntityDecl ea && b is EntityDecl eb) return Equal(ea, eb);
        if (a is ValueTypeDecl va && b is ValueTypeDecl vb) return Equal(va, vb);
        if (a is EnumDecl na && b is EnumDecl nb) return Equal(na, nb);
        return false;
    }

    private static bool Equal(EntityDecl a, EntityDecl b)
    {
        if (a.Name != b.Name) return false;
        if (a.Version != b.Version) return false;
        if (!Equal(a.Deprecates, b.Deprecates)) return false;
        if (!Equal(a.Identity, b.Identity)) return false;
        if (!SequenceEqual(a.Relations, b.Relations, Equal)) return false;
        if (!SequenceEqual(a.Properties, b.Properties, Equal)) return false;
        if (!Equal(a.Lifecycle, b.Lifecycle)) return false;
        if (!SequenceEqual(a.Events, b.Events, Equal)) return false;
        if (!SequenceEqual(a.Commands, b.Commands, Equal)) return false;
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(ValueTypeDecl a, ValueTypeDecl b)
    {
        if (a.Name != b.Name) return false;
        if (a.Version != b.Version) return false;
        if (!SequenceEqual(a.Fields, b.Fields, Equal)) return false;
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(EnumDecl a, EnumDecl b)
    {
        if (a.Name != b.Name) return false;
        if (a.Version != b.Version) return false;
        if (a.Variants.Length != b.Variants.Length) return false;
        for (int i = 0; i < a.Variants.Length; i++)
        {
            if (a.Variants[i] != b.Variants[i]) return false;
        }
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(DeprecatesClause? a, DeprecatesClause? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Version == b.Version && a.UntilIso8601 == b.UntilIso8601;
    }

    private static bool Equal(IdentityDecl a, IdentityDecl b) =>
        a.FieldName == b.FieldName && Equal(a.Type, b.Type);

    private static bool Equal(RelationDecl a, RelationDecl b)
    {
        if (a.Name != b.Name) return false;
        if (a.TargetEntity != b.TargetEntity) return false;
        if (a.IsOptional != b.IsOptional) return false;
        if (a.Cardinality != b.Cardinality) return false;
        if (a.Semantic != b.Semantic) return false;
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(PropertyDecl a, PropertyDecl b)
    {
        if (a.Name != b.Name) return false;
        if (!Equal(a.Type, b.Type)) return false;
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(FieldDecl a, FieldDecl b) =>
        a.Name == b.Name && Equal(a.Type, b.Type);

    private static bool Equal(LifecycleDecl a, LifecycleDecl b)
    {
        if (a.States.Length != b.States.Length) return false;
        for (int i = 0; i < a.States.Length; i++)
        {
            if (a.States[i] != b.States[i]) return false;
        }
        if (!SequenceEqual(a.Transitions, b.Transitions, Equal)) return false;
        return true;
    }

    private static bool Equal(TransitionDecl a, TransitionDecl b) =>
        a.From == b.From && a.To == b.To && a.OnEvent == b.OnEvent;

    private static bool Equal(EventDecl a, EventDecl b)
    {
        if (a.Name != b.Name) return false;
        if (!SequenceEqual(a.Payload, b.Payload, Equal)) return false;
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(CommandDecl a, CommandDecl b)
    {
        if (a.Name != b.Name) return false;
        if (!SequenceEqual(a.Arguments, b.Arguments, Equal)) return false;
        if (a.ReturnsType != b.ReturnsType) return false;
        if (a.SideEffectEvent != b.SideEffectEvent) return false;
        if (!SequenceEqual(a.Annotations, b.Annotations, Equal)) return false;
        return true;
    }

    private static bool Equal(TypeRef a, TypeRef b)
    {
        if (a is PrimitiveTypeRef pa && b is PrimitiveTypeRef pb)
        {
            return pa.Kind == pb.Kind && pa.IsOptional == pb.IsOptional && pa.IsArray == pb.IsArray;
        }
        if (a is NamedTypeRef na && b is NamedTypeRef nb)
        {
            return na.Name == nb.Name && na.IsOptional == nb.IsOptional && na.IsArray == nb.IsArray
                && na.Version == nb.Version;
        }
        return false;
    }

    private static bool Equal(AnnotationDecl a, AnnotationDecl b)
    {
        if (a.Namespace != b.Namespace) return false;
        if (a.Name != b.Name) return false;
        if (a.Arguments.Count != b.Arguments.Count) return false;
        foreach (var kv in a.Arguments)
        {
            if (!b.Arguments.TryGetValue(kv.Key, out var bv)) return false;
            if (!Equal(kv.Value, bv)) return false;
        }
        return true;
    }

    private static bool Equal(AnnotationValue a, AnnotationValue b) => (a, b) switch
    {
        (AnnotationStringValue x, AnnotationStringValue y) => x.Value == y.Value,
        (AnnotationIntValue x, AnnotationIntValue y) => x.Value == y.Value,
        (AnnotationDecimalValue x, AnnotationDecimalValue y) => x.Value == y.Value,
        (AnnotationBoolValue x, AnnotationBoolValue y) => x.Value == y.Value,
        (AnnotationIdentValue x, AnnotationIdentValue y) => x.Value == y.Value,
        _ => false
    };

    private static bool SequenceEqual<T>(ImmutableArray<T> a, ImmutableArray<T> b, System.Func<T, T, bool> cmp)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!cmp(a[i], b[i])) return false;
        }
        return true;
    }
}
