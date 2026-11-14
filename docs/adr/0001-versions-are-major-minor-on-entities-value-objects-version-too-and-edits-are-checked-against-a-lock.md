# Versions are major.minor on entities, value objects version too, and edits are checked against a lock

Status: accepted · Date: 2026-11-14

Gravity versioned entities with a single integer, chained them with `deprecates version N until
"date"`, and ran its compatibility rules (VAL020–VAL030) only between two declarations present in
the source at once. That left three holes for a model whose events are stored for years: an edit
inside a version was compared to nothing, a value object could not be versioned in the same way an
entity could, and an expired deprecation failed the build although the messages it described were
still in the stream. The grammar changes below close them. They were decided with the first
consumer of the feature, the EzyFlow canonical domain (canonical-domain ADR 0005 and 0006).

## Decided

1. **An entity's version is `major.minor`.** `entity X version 2.1`. A bare integer reads as
   `N.0`. The major is what `deprecates` chains and what references pin; the minor counts the
   accepted minor changes inside a major.
2. **Value types and enums version like entities, by major only.** `type Money version 2`, with a
   `deprecates version 1` chain. They carry no minor of their own: a minor change to one is a minor
   change to every entity whose closure contains it.
3. **A reference must name its version once the target has more than one.** An unqualified
   reference to a type with two versions is an error; it no longer resolves to the highest. A past
   major must keep meaning what it meant.
4. **`until` is optional.** A deprecation without a date never expires. VAL030 fires only where a
   date was written.
5. **A new major declares a `migrate` block, in both directions.** Renames are derived both ways.
   A field added states its default (upward); a field removed states the value older readers get
   (downward). Anything the compiler cannot
   derive is declared as a hand-written step, in both directions. A major with an undeclared
   difference does not compile.
6. **An added enum value declares its fallback, in a minor as in a major.** It is the existing
   value handed to a reader that does not know the new one. An enum value added without one does
   not compile.
7. **A lock file holds the accepted shape of every entity's closure** at its current major and
   minor, and is committed with the model. On every build the compiler compares the source to it:
   no difference passes; a minor difference passes only if the minor was raised and the lock
   re-accepted; a major difference is refused.
8. **The lock marks each version shipped or unshipped.** An unshipped version may be re-accepted
   with any change. A shipped one is frozen. Marking is an explicit compiler command, meant for the
   model's release workflow.
9. **Emitters receive every major**, with its pinned closure, its migration steps and the minor
   at which each enum value appeared, so a target
   can generate past shapes and the conversions between adjacent majors. What a target names them
   is the emitter's business.

## Considered options

- Comparing against the last published artefact, or against version control history: rejected. The
  first needs network access at build time and compares against what was published, not what was
  accepted; the second breaks on squash merges and shallow clones.
- A compiler-owned history of past shapes instead of old declarations kept in source: rejected.
  The declarations already are that history, readable and diffable, and the existing diff rules
  run on them unchanged.
- Minor numbers on value objects: rejected. Nothing reads a value object on its own; the number
  that matters is the one on the entity that embeds it.
- Migration in one direction: rejected. A model may be served to readers that cannot be upgraded
  with it.

## Consequences

- VAL020–VAL030 keep their meaning between coexisting declarations; the lock comparison reuses the
  same classification for edits in place.
- A bare integer is `N.0`, so existing declarations keep parsing. A build with no lock file is an
  error: a missing lock must never read as "nothing to check". A model creates its lock once,
  with an explicit command that accepts the current shapes as unshipped.
- A model with a single version of everything never writes a version on a reference.

Sources: `docs/specs.md` §4.4; `Gravity.Dsl.Compiler/Versioning/VersionDiff.cs`, `DiffRules.cs`;
`Validation/RuleIds.cs`; `Parsing/Parser.cs`; `Resolution/Resolver.cs`; canonical-domain ADR 0005
and 0006.
