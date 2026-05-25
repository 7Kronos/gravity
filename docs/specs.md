# Gravity DSL — v1 Proposal

**Status:** Draft for review
**Scope:** Textual definition language that declares domain entities — their identity, relations, properties, lifecycle, events, and commands — and drives multi-target codegen. Foundation for the Gravity Registry's governance layer.
**First sample entities:** `Employee`, `TimeEntry`, `Project`.

---

## 1. Background

Enterprise software is increasingly built by AI coding tools instructed in plain language. Left ungoverned, this produces semantic entropy: every team, every project, every prompt reinvents what "Employee" or "Project" means, with slightly different fields, slightly different rules, slightly different shapes. Across an enterprise, the same business concept fragments into dozens of incompatible variants. The data layer fills up with near-duplicates. APIs drift. Integrations break. Audits become impossible.

Gravity is a response to that entropy. It is a definition-first approach: domain experts declare what business concepts mean, in precise terms, before any code gets written. AI coding tools then consume those definitions read-only. They implement; they do not invent.

For this to work, definitions need a representation. Something a human can author or review, an AI can consume reliably, and a compiler can validate. Something precise enough that there is no room left for downstream reinterpretation, and portable enough that the same source drives C# records, GraphQL schemas, OpenAPI specs, event contracts, and validation schemas — all from one truth.

That is what Gravity DSL is.

The Gravity Registry — the surrounding governance layer that adds scopes, permissions, rules, releases, and the Library of reference patterns — composes definitions written in this DSL into governed bundles, gates releases, and assists domain architects with AI-driven proposals. The Registry depends on the DSL. The DSL does not depend on the Registry. This proposal is for the DSL alone.

## 2. Design principles

These hold for v1 and beyond. Departures require explicit decision.

1. **The DSL is the spec.** Every downstream artifact is generated. C# records, JSON Schema, GraphQL SDL, OpenAPI, AsyncAPI, and anything else — all derived from the same `.gravity` source. No hand-authored surface that could drift.

2. **Domain-only.** The DSL declares what concepts mean, not how they are stored, transported, or deployed. Storage layout, broker topology, deployment targets, runtime infrastructure — all live downstream. The DSL declares "Employee has `hireDate: Date`"; whether that becomes a Postgres column, a JSONB field, or a Mongo document is the emitter's or projection layer's concern.

3. **Read-only at build time.** The DSL is authored by humans, assisted by AI as proposer and interviewer. At build time, AI coding tools consume DSL-generated artifacts read-only. This asymmetry is the central governance mechanism. The DSL is designed to make the read-only contract precise and machine-checkable.

4. **Additive-only versioning by default.** Per-entity version numbers; the toolchain refuses breaking changes without an explicit deprecation clause. This protects every downstream consumer — every codebase that has linked a definition version — from silent semantic shifts.

5. **AI-readable.** The grammar is readable by current LLMs without exotic tooling. A translation from "system X's schema for Employee" into "canonical Gravity Employee" can be drafted by an LLM and reviewed by a human before it enters a registry.

6. **Pluggable, not prescriptive.** Codegen targets are extensions. The DSL ships with a reference set of emitters; everything else is a plugin. New languages, new API surfaces, new storage backends — written and distributed by whoever needs them, against a stable AST interface.

7. **Composable upward.** Designed to be consumed by the Gravity Registry (or any governance layer) without being coupled to it. The DSL has no syntax for scopes, permissions, releases, or library imports — those are Registry concerns. A single `.gravity` file is a useful, complete artifact on its own.

## 3. Scope (v1)

**In scope:**
- DSL grammar (textual, custom; final shape crystallizes during the Phase 0 spike)
- Lexer, parser, AST, resolver, validator
- Pluggable emitter architecture with a versioned, stable AST interface
- Five reference emitters: C# records, JSON Schema, GraphQL SDL, OpenAPI, AsyncAPI
- Per-entity versioning with additive-only enforcement
- Three sample entities defined in DSL: `Employee`, `TimeEntry`, `Project`
- Build-time integration: standalone CLI first, MSBuild target second
- Documentation: language reference, emitter authoring guide, sample registry

**Out of scope (separate work):**
- Scopes, permissions, rules, releases, library — these are Gravity Registry concerns
- Integration with the Gravity Registry (separate proposal once the DSL stabilizes)
- AI-driven definition authoring tools (separate proposal)
- LSP / editor tooling
- DSL formatter / linter
- Cross-language compiler implementations (the v1 compiler is C# only)
- Runtime DSL evaluation (DSL is compile-time only)
- Postgres DDL emitter (likely community-authored once the architecture stabilizes)

## 4. DSL syntax (working draft)

The exact syntax crystallizes during the Phase 0 spike. The shape below is the working hypothesis.

### 4.1 File structure

A `.gravity` file declares one or more entities and value types. Files can import other files for shared types.

```gravity
namespace hr;

import "common/contact.gravity";
import "common/money.gravity";

entity Employee version 1 {
  // ...
}
```

A namespace groups related definitions but has no governance meaning at the DSL level — that is what Scopes are for in the Registry.

### 4.2 Entity declaration

```gravity
entity TimeEntry version 1 {

  identity id: UUID;

  relations {
    employee:  Employee  cardinality one;
    project:   Project   cardinality one;
    submitter: Employee  cardinality one  semantic submitted_by;
    approver:  Employee? cardinality one  semantic approved_by;
  }

  properties {
    date:              Date;
    hours:             Decimal;
    billable:          Boolean;
    description:       String?;
    rejection_reason:  String?;
  }

  lifecycle {
    states {
      Draft, Submitted, Approved, Rejected, Reopened;
    }
    transitions {
      Draft     -> Submitted on Submitted;
      Submitted -> Approved  on Approved;
      Submitted -> Rejected  on Rejected;
      Rejected  -> Draft     on Resubmitted;
      Approved  -> Reopened  on Reopened;
      Reopened  -> Submitted on Submitted;
    }
  }

  events {
    Submitted   { submitted_at: DateTime; };
    Approved    { approver_id: UUID; approved_at: DateTime; };
    Rejected    { approver_id: UUID; reason: String; };
    Resubmitted {};
    Reopened    { reopener_id: UUID; reason: String; };
  }

  commands {
    Submit()
      returns SubmissionResult
      with side_effect Submitted;

    Approve(approver_id: UUID)
      returns ApprovalResult
      with side_effect Approved;

    Reject(approver_id: UUID, reason: String)
      returns RejectionResult
      with side_effect Rejected;
  }
}
```

`Employee` and `Project` are declared in companion files with similar structure. `Employee` carries lifecycle states like `Onboarding`, `Active`, `OnLeave`, `Terminated`; `Project` carries `Planned`, `Active`, `OnHold`, `Completed`, `Cancelled`. Both are referenced by `TimeEntry` through the `relations` block.

### 4.3 Type system

Primitives: `String`, `Int`, `Long`, `Decimal`, `Boolean`, `Date`, `DateTime`, `UUID`. Optional via `?` suffix. Arrays via `[]` suffix. User-defined value types and enums:

```gravity
type ContactInfo {
  email:      String;
  phone:      String?;
  preferred:  ContactMethod;
}

enum ContactMethod { Email, Phone, None }

enum ContractType { CDI, CDD, Freelance, Intern, Apprentice }
```

### 4.4 Versioning

Entities and value types declare a version. Additive changes are allowed within a version. Breaking changes require a new version with explicit deprecation:

```gravity
entity Employee version 2
  deprecates version 1 until "2026-12-31"
{
  // ...
}
```

The toolchain refuses to compile if v2 makes a breaking change against v1 without `deprecates`. Breaking changes are: field removal, type narrowing, lifecycle state removal, command removal, and semantic-changing default changes.

### 4.5 Emitter directives

Emitters may need target-specific hints — a GraphQL emitter might want a custom resolver name; a JSON Schema emitter might want a format hint. Rather than baking these into the core grammar, the DSL supports namespaced annotations that emitters can consume:

```gravity
properties {
  email: String @json_schema(format: "email") @graphql(searchable: true);
}
```

The compiler validates that annotation namespaces are claimed by a registered emitter; emitters validate their own annotation shapes. This keeps the core grammar small and lets emitters evolve independently of the language.

## 5. Pluggable emitter architecture

The compiler core produces a versioned, resolved AST. Emitters consume the AST and produce artifacts. The DSL ships with a reference set; everything else is a plugin.

### 5.1 The emitter contract

An emitter is a .NET assembly implementing a stable `IEmitter` interface. It declares a unique target name (e.g. `csharp`, `json-schema`, `community/typescript`), a configuration schema so users can configure output paths and naming conventions uniformly, an annotation namespace it claims so other emitters cannot squat on its keys, and the AST version it consumes so the compiler can refuse incompatible emitters with a clear error.

The AST interface is versioned independently of the DSL grammar. A grammar can add new constructs in an additive way that older emitters continue to handle correctly; breaking AST changes bump the AST version and require emitters to update against a documented migration path.

### 5.2 Reference emitters shipped with v1

- **C# records.** Records for events, commands, command responses, entity state types, and value types. Generated namespaces mirror the DSL namespace.
- **JSON Schema.** Per-event and per-command schemas for ingestion-side validation, suitable for HTTP gateways or message validators.
- **GraphQL SDL.** Per-entity types, field signatures matching `relations`, enums for lifecycle states, subscription channels per event.
- **OpenAPI.** HTTP RPC surface: `POST /events/<event_name>`, `POST /commands/<command_name>`, with request and response schemas plus idempotency-key conventions.
- **AsyncAPI.** Broker-agnostic event contracts: payload schemas, channel naming conventions, ordering and delivery semantics declared per event. Users configure subject patterns (NATS, Kafka, RabbitMQ topics) via emitter config — the DSL itself stays broker-neutral.

### 5.3 Discovery and invocation

Emitters live in NuGet packages. A `.gravity.yaml` file in the project root (legacy filename `.gravity.config` still accepted, with a `CFG005` deprecation warning) declares which emitters are enabled and how they are configured:

```yaml
emitters:
  csharp:
    output: gen/csharp
    namespace: AcmeCo.Domain
  json-schema:
    output: gen/schemas
  community/typescript:
    output: gen/ts
    package: "acme-domain-types"
```

The CLI invokes them all in parallel; the MSBuild target does the same at build time.

### 5.4 Deterministic output

All reference emitters produce byte-identical output for identical input: sorted keys, no timestamps, stable iteration order. This is required because generated artifacts are checked into source control alongside hand-written code. Third-party emitters are strongly encouraged to follow the same rule and are tested for it via the golden-file harness shipped with the emitter authoring guide.

## 6. Architecture

### 6.1 Toolchain stages

The compiler runs in seven stages: lex source into tokens; parse tokens into an AST; resolve names, types, and cross-file imports; validate (additive-only version checks, lifecycle consistency, no orphan references, annotation-namespace ownership); publish the resolved AST through the stable interface; invoke registered emitters; emit artifacts deterministically.

### 6.2 Implementation language

C#. The reasons: modern .NET has solid parser tooling (Sprache, Pidgin, Superpower); the toolchain packages cleanly as both a CLI tool and (in a future milestone) a Roslyn source generator for in-IDE generation; the emitter plugin model maps directly to .NET assembly loading. The DSL itself is language-neutral — only the compiler is C#-bound. Emitters can target any language.

### 6.3 Project layout

```
Gravity.Dsl/
├── Gravity.Dsl.Compiler/             # Lexer, parser, AST, resolver, validator
├── Gravity.Dsl.Ast/                  # Public AST interface (NuGet-published)
├── Gravity.Dsl.Emitter/              # Emitter contract and host
├── Gravity.Dsl.Emitter.CSharp/       # C# reference emitter
├── Gravity.Dsl.Emitter.JsonSchema/   # JSON Schema reference emitter
├── Gravity.Dsl.Emitter.GraphQL/      # GraphQL SDL reference emitter
├── Gravity.Dsl.Emitter.OpenApi/      # OpenAPI reference emitter
├── Gravity.Dsl.Emitter.AsyncApi/     # AsyncAPI reference emitter
├── Gravity.Dsl.Cli/                  # `gravc` CLI driver
├── Gravity.Dsl.MsBuild/              # MSBuild integration
├── Gravity.Dsl.Tests/                # Unit + integration + golden-file tests
└── samples/registry/                 # Employee, TimeEntry, Project DSL files
```

### 6.4 Build integration

The standalone CLI is for ad-hoc work and CI: `gravc gen --input registry/ --output gen/`. The MSBuild target is for generation at build time, declared in csproj as `<GravityDsl Include="registry/*.gravity" />`. Both drivers share the compiler, the emitter host, and the emitters themselves; only the entry point differs.

## 7. Phased plan

| Phase | Deliverable | Estimate |
|---|---|---|
| 0 | Spike: working-draft grammar for Employee, TimeEntry, Project; hand-derived sample artifacts; parser library decision; AST interface sketch | 1–2 weeks |
| 1 | Compiler core: lexer, parser, AST, resolver, validator; round-trip tests pass | 2–3 weeks |
| 2 | AST publication + emitter host: stable interface, plugin discovery, configuration, parallel invocation | 1 week |
| 3 | C# reference emitter | 1 week |
| 4 | JSON Schema reference emitter | 3–4 days |
| 5 | GraphQL SDL reference emitter | 1 week |
| 6 | OpenAPI reference emitter | 3–4 days |
| 7 | AsyncAPI reference emitter | 3–4 days |
| 8 | Versioning checks: additive-only enforcement, deprecation flags, version-pair comparison | 1 week |
| 9 | Build integration, CLI ergonomics, deterministic output, error reporting; emitter authoring guide and reference sample | 1 week |
| 10 | Open-source launch: docs site, contribution guide, NuGet packages, sample registry repo | 1 week |

Roughly three to four months of focused work for a single contributor. Phases 3–7 are mostly independent once the emitter host is in place and can run in parallel with multiple contributors.

## 8. Risks

**Grammar churn during early phases.** Real definitions will surface gaps the spike did not anticipate. Mitigation: explicit Phase 0, peer review, willingness to break compatibility on the grammar before v1.0 ships.

**Generated code quality.** Auto-generated artifacts can be ugly or hard to debug. Mitigation: invest in correct indentation, doc comments, and namespacing in the reference emitters. Generated artifacts should look hand-written. The emitter authoring guide documents the bar.

**AST interface premature ossification.** The AST is a public contract. Locking it down too early constrains the grammar; locking it down too late breaks third-party emitters. Mitigation: explicit AST versioning, a documented deprecation policy, integration tests across the reference emitters in every PR.

**Emitter sprawl.** A pluggable architecture invites quantity over quality. Mitigation: a clear emitter authoring guide, the golden-file harness, and an optional badge program for emitters that meet quality bars (deterministic output, idiomatic generated code, passing the conformance suite).

**Versioning model tested late.** Phase 8 is where additive-only enforcement lands, but the underlying machinery must be designed in from the start. Mitigation: the AST and validator architecture must support version-pair comparison from Phase 1.

**Coupling drift with Gravity Registry.** The DSL must stay useful standalone even as the Registry evolves. Mitigation: keep Registry concerns (scopes, permissions, library) out of the grammar; the Registry composes DSL artifacts rather than extending the language.

## 9. Open questions

**Parser library.** Sprache, Pidgin, Superpower, or hand-rolled? Decided in the Phase 0 spike.

**File extension.** `.gravity` is proposed. Alternatives: `.grav`, `.gdl`, `.def`. Bikeshed in Phase 0.

**CLI name.** `gravc` is proposed (Gravity compiler). Alternatives: `gravity-dsl`, `gdsl`, or a `gravity dsl ...` subcommand under an umbrella tool shared with the Registry. Best resolved when the Registry CLI design lands.

**Source generator vs CLI-first.** The MSBuild target is the long-term right answer; the CLI is faster to ship. Plan: CLI first, source generator as follow-up.

**Lifecycle enforcement scope.** Generated lifecycle state enums are straightforward. Generated runtime guards (refuse `Approved → Draft` at runtime) are more invasive. Plan: enum in v1, runtime guard as opt-in per-emitter in v2.

**Cross-entity validation rigor.** Detect that `TimeEntry.project: Project` references a defined entity? Yes — with clean error messages distinguishing missing-import from missing-definition.

**License.** Apache 2.0 is the conservative default for a project meant to become a standard; MIT is more permissive. Decision deferred but defaulting to Apache 2.0 unless there is a strategic reason otherwise.

---

**Authoring:** Gravity team. v1 draft.
**Origin:** The grammar shape derives from patterns crystallized during work on an event-driven integration platform. The DSL is now independent of that origin and stewarded as part of the Gravity open-source ecosystem.
**Successor artifacts:** `specs/001-gravity-dsl/{spec.md, plan.md, tasks.md}` — SpecSwarm-ready playbook for `/sw:implement`, once this proposal is reviewed and locked.