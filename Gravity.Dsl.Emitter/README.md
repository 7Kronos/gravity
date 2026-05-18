# Gravity.Dsl.Emitter

Emitter host and `IEmitter` contract for the [Gravity DSL](https://github.com/).
This is the package every Gravity codegen target references. It pairs with
`Gravity.Dsl.Ast`: the AST package supplies the read-only model an emitter
consumes; this package supplies the contract the emitter implements and the
host that invokes it.

## Public surface

- `IEmitter` — implement this on a class with a public parameterless constructor
  for the host to discover via `EmitterRegistry.Discover`.
- `IEmitterOutput` — sink an emitter writes its files into.
- `EmitResult` — diagnostics returned from an emitter; the host sorts them.
- `EmitterConfig`, `EmitterConfigSchema`, `ConfigKey`, `ConfigValueKind` — the
  schema-driven configuration block.
- `SemanticVersionRange` — small semver-range parser used by
  `IEmitter.SupportedAstVersions`. Supports `>=`, `>`, `<=`, `<`, `=` composed
  with space-separated AND, e.g. `">=1.0.0 <2.0.0"`.
- `EmitterRegistry` — discovery, ownership checks (`HOST001`, `HOST002`), and
  `ClaimedAnnotationNamespaces()` for the validator.
- `EmitterHost.Run(...)` — pre-flight checks (`HOST003`), parallel invocation,
  diagnostic sorting, deterministic commit.
- `BufferedEmitterOutput` — in-memory sink the host hands each emitter.
- `ConfigLoader` — `.gravity.config` YAML loader (`CFG001..CFG003`).

## Determinism

The host commits each emitter's buffer in ordinal relative-path order with UTF-8
(no BOM) and LF line endings. Emitters that follow these constraints meet the
project-wide determinism bar (constitution — Architectural constraints,
Deterministic output).

## License

Apache-2.0.
