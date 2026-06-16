# Examples

All files here are written in the **canonical form** produced by the compiler's
`SourceWriter` (see `../reference/canonical-format.md`). Parsing any of them and
re-serializing yields the identical bytes.

- **`timesheet.gravity`** — a complete entity exercising every section: identity,
  relations (incl. `semantic` and optional), properties, lifecycle, events
  (incl. an empty-payload `Resubmitted {};`), and commands.
- **`versioned.gravity`** — version-qualified type references (`Money@2`, `@2?`,
  `@2[]`, `@2?[]`) and an additive `version 2` entity with a `deprecates ... until`
  clause.
- **`annotated.gravity`** — namespaced annotations in every legal position:
  leading on a top-level entity, leading on a relation, trailing on properties
  (with multiple, ordinal-sorted arguments).

These mirror, in canonical form, the hand-authored samples under the repo's
`samples/registry/` and `Playground/` (which use aligned multi-space padding the
serializer collapses to single spaces).
