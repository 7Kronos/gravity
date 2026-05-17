Establish the constitution for Gravity DSL — a textual definition language
for AI-constrained enterprise software development. The DSL declares domain
entities (identity, relations, properties, lifecycle, events, commands) in
a precise, versioned form, and drives multi-target codegen through a
pluggable emitter architecture. It is the foundation of the Gravity
open-source ecosystem and the underlying definition format for the Gravity
Registry, which adds scopes, permissions, releases, and the Library of
reference patterns on top.

This constitution governs all subsequent specifications, plans, and
implementation decisions for the Gravity DSL project.

## Mission

Gravity DSL exists to solve the semantic entropy that emerges when AI
coding tools generate enterprise software without governed shared meaning.
By declaring domain concepts in a precise, machine-validated, human-authored
form before any code is written, the DSL enables a definition-first
approach where AI tools implement against a fixed semantic contract rather
than inventing their own.

## Core principles (non-negotiable)

**I. The DSL is the spec.** Every downstream artifact — language types,
schemas, API surfaces, event contracts, validators — is generated from
`.gravity` source. No hand-authored target surface that could drift from
the source of truth is acceptable. If an emitter cannot produce a needed
surface, the DSL or emitter is extended; surfaces are never authored
independently.

**II. Domain-only.** The DSL declares what concepts mean, never how they
are stored, transported, or deployed. Storage layout, broker topology,
deployment targets, and runtime infrastructure are downstream concerns.
Any proposal to add storage hints, deployment metadata, or topology
information to the core grammar must be refused; such concerns belong in
emitter configuration or in the Gravity Registry's governance layer.

**III. Read-only at build time.** The DSL is authored by humans, assisted
by AI as proposer and interviewer. AI coding tools at build time consume
DSL-generated artifacts read-only. This asymmetry is the central
governance mechanism and must remain machine-enforceable: generated
artifacts carry no machinery that would let downstream code mutate or
extend domain definitions.

**IV. Additive-only versioning by default.** Per-entity version numbers.
Breaking changes — field removal, type narrowing, lifecycle state removal,
command removal, semantic-changing default changes — are refused by the
toolchain without an explicit `deprecates` clause and a deprecation window.
This protects every downstream consumer from silent semantic shifts.

**V. AI-readable.** The grammar must be readable and writable by current
LLMs without exotic tooling. Decisions that improve human authoring at the
cost of AI legibility (or vice versa) require explicit justification. The
translation from "system X's schema" into "canonical Gravity entity" must
remain a tractable LLM task.

**VI. Pluggable, not prescriptive.** Codegen targets are extensions, never
core. The DSL ships with a curated reference set of emitters; all other
targets are plugins implementing a stable AST interface. The core grammar
must not accumulate target-specific constructs. Target-specific hints use
namespaced annotations whose ownership is registered by the consuming
emitter.

**VII. Composable upward.** The DSL must remain useful and complete on its
own, without the Gravity Registry. Registry concerns — scopes, permissions,
rules, releases, library imports — do not appear in the DSL grammar. The
Registry composes DSL artifacts; it does not extend the language. Any
feature request that would couple the DSL to the Registry must be
redirected to the Registry layer.

## Architectural constraints

**Implementation language.** The reference compiler is written in C#. This
is fixed for v1 and revisited only if the .NET ecosystem becomes a
material barrier to community contribution.

**Stable AST contract.** The compiler publishes a versioned AST through a
public NuGet-distributed interface. The AST version is independent of the
DSL grammar version. Breaking AST changes require a documented migration
path and a deprecation window for third-party emitters.

**Deterministic output.** All reference emitters produce byte-identical
artifacts for identical input: sorted keys, no timestamps, stable
iteration order. Third-party emitters are expected to meet the same bar
and are tested against it.

**Build integration parity.** The standalone CLI and the MSBuild target
share the compiler, the emitter host, and the emitters themselves.
Behavior must be identical across the two entry points.

**Out-of-scope discipline.** The following are explicitly excluded from
the DSL and must not creep in:
- Scopes, permissions, rules, releases, library imports (Registry concerns)
- Storage backends, projection layouts, query optimization
- Runtime topology, deployment targets, broker configuration
- AI-driven authoring tools (separate proposal)
- LSP, formatter, linter (out of scope for v1)
- Runtime DSL evaluation (DSL is compile-time only)

## Quality standards

**Testing.** Three tiers, enforced in CI:
- Round-trip tests for the parser: every valid source produces an AST
  that can be re-emitted to source that re-parses to the same AST.
- Golden-file tests for each reference emitter: a curated set of DSL
  inputs maps to byte-checked output files. Changes to emitter output
  require deliberate updates to the golden files with review.
- Integration tests across the full reference emitter set, run on every
  PR, to catch AST changes that break any emitter.

**Generated artifacts look hand-written.** Indentation is correct. Doc
comments are present and useful. Namespaces are idiomatic. A reviewer
reading generated C#, GraphQL, or OpenAPI should not be able to tell at a
glance that it was generated.

**Error messages.** Compiler errors carry source location, the rule
violated, and (where possible) the fix. Missing imports are distinguished
from missing definitions. Annotation namespace conflicts name both
claimants.

**Backwards compatibility.** Once v1.0 ships, the grammar and AST follow
semantic versioning. Pre-v1.0 churn is acceptable and expected; the
project must communicate it clearly to early adopters.

## Amendment policy

Changes to this constitution require a written rationale recorded
alongside the change. Departures from the seven core principles or the
architectural constraints require explicit, documented justification.
Decisions made under this constitution are traceable from the
`specs/<feature>/` artifacts that cite it.