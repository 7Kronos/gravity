# Gravity.Dsl.Ast

Public, versioned AST records for the [Gravity DSL](https://github.com/) — a textual
definition language for AI-constrained enterprise software development.

This is the **read-only contract emitters consume**. The Gravity compiler parses,
resolves, and validates `.gravity` source, then publishes an instance of these
records to any registered emitter through the `Gravity.Dsl.Emitter` host. Emitters
do not depend on the compiler; they depend on this package and on `Gravity.Dsl.Emitter`.

## AST version contract

The package exposes a single constant:

```csharp
public static class AstVersion
{
    public const string Value = "1.0.0";
}
```

`AstVersion` is **independent of the DSL grammar version**. The grammar can grow
additively without changing the AST version; only a breaking change to a public
record on this package bumps the major.

Emitters declare the AST version range they support (see
`Gravity.Dsl.Emitter.IEmitter.SupportedAstVersions`). The emitter host refuses
incompatible emitters with a clear error at startup. This is how the Gravity
project keeps third-party emitters working across grammar revisions.

## Additive-only versioning policy (AST level)

- A new optional field on an existing record is **non-breaking** if it has a default
  that preserves the prior semantics. Such a change is shipped in a minor version
  of this package and does **not** bump `AstVersion`'s major.
- A new record type is **non-breaking** and ships in a minor version.
- Removing a field, narrowing a field's type, or changing a record's shape in a
  way that existing emitters cannot tolerate is **breaking** and requires:
  1. A major bump of `AstVersion.Value`.
  2. A documented migration path for third-party emitters.
  3. A deprecation window in which both AST versions are published in parallel.

This policy mirrors the DSL's own additive-only-by-default principle (constitution
Principle IV) at the AST contract level.

## Read-only contract (Principle III, VI)

Every record is a C# `record` with `init`-only properties; every collection is an
`ImmutableArray<T>` or `ImmutableSortedDictionary<TKey, TValue>`. There is no API
on this package that lets downstream code redefine domain meaning at build time —
that is the central governance mechanism of the Gravity DSL.

In particular, `AnnotationDecl.Arguments` uses `ImmutableSortedDictionary` (not
`ImmutableDictionary`) so iteration order is byte-stable across runs and platforms.
This is required for deterministic emitter output and is part of the contract.

## Compatibility

- Target framework: `net9.0`.
- No external dependencies beyond `System.Collections.Immutable` (BCL).

## License

Apache-2.0. See `LICENSE` in the repository root.
