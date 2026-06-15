# MyEmitter — Gravity DSL emitter

Scaffolded by `dotnet new gravity-emitter`. This is a custom Gravity DSL
emitter plugin packaged for NuGet distribution.

## Layout

| Path | Purpose |
| --- | --- |
| `MyEmitter.csproj` | NuGet package definition with deterministic-build properties. |
| `MyEmitter.cs` | `IEmitter` implementation — replace the body of `Emit` with your rendering. |
| `buildTransitive/MyEmitter.props` | Cross-package wiring; auto-registers this emitter when `Gravity.Dsl.MsBuild` is also referenced. |

## Build and consume

```bash
dotnet pack -c Release -o ./nupkg
```

In a consuming project:

```xml
<ItemGroup>
  <PackageReference Include="Gravity.Dsl.MsBuild" />
  <PackageReference Include="MyEmitter" />
</ItemGroup>
```

And in the consumer's `.gravity.yaml`:

```yaml
emitters:
  target-name-placeholder:
    output: gen/target-name-placeholder
```

`dotnet build` on the consumer will then run your emitter alongside the
built-in C# reference emitter.

## Where to read next

- `docs/emitter-authoring-guide.md` in the Gravity repo — the full author's
  reference. Covers determinism, golden-file testing, version pinning, the
  configuration schema, and the CI / quality checklist (§12) you should
  meet before shipping.
- `samples/emitters/outline/` in the Gravity repo — the canonical
  copy-paste source for richer rendering (namespace-aware directory layout,
  per-declaration-type renderers).
