# Outline emitter (sample)

This is a minimal example, not a production target. It exists to demonstrate the
`IEmitter` contract end-to-end so community emitter authors have a working
template to copy from. It emits one Markdown file per entity (plus minimal files
per value type and enum) under a configurable output directory; the rendering is
deliberately tiny so the surface area you have to read before extending it stays
small.

If you need a real Markdown target (e.g. one tuned for a documentation site), do
not extend this project in place — start a sibling project under the repo root
next to `Gravity.Dsl.Emitter.CSharp/`, copy the relevant bits, and drop the
`.Sample.` segment from the NuGet id. The repo-layout signal (`samples/`) and
the `.Sample.` package-id segment are permanent commitments per LD-12.

See `specs/003-phase-9-build-integration/spec.md` §4.3 (FR-220..FR-225) for the
full sample-emitter contract and `specs/003-phase-9-build-integration/plan.md`
§3.6 / §3.7 for the rendering shape.
