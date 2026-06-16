---
name: gravity-dsl
description: >-
  Read and write Gravity DSL (.gravity) source perfectly. Use when authoring,
  editing, reviewing, or generating .gravity files — declaring domain entities
  (identity, relations, properties, lifecycle, events, commands), value types,
  enums, versioning/deprecation, and namespaced annotations — or when you need
  the exact grammar, canonical formatting, or validation rules of the Gravity
  reference compiler.
---

# Gravity DSL — read & write

Gravity DSL is a textual definition language that declares domain entities and
drives multi-target codegen. A `.gravity` file is the single source of truth;
everything downstream (C#, JSON Schema, Postgres DDL, …) is generated from it.
This skill lets you read any `.gravity` file and write byte-perfect, canonically
formatted, compiler-valid source.

The grammar here is transcribed from the reference compiler — the lexer
(`TokenKind.cs`, `Lexer.cs`), the recursive-descent parser (`Parser.cs`), the
canonical serializer (`SourceWriter.cs`), and the validator
(`Validator.cs` / `RuleIds.cs`). When in doubt, those files are authority.

## When you need more detail

- **`reference/grammar.md`** — full EBNF, every token, lexical rules, the `@N`
  version-suffix rules. Read this to parse/understand arbitrary source.
- **`reference/canonical-format.md`** — the exact byte-level formatting the
  compiler's `SourceWriter` produces. Read this when *writing* source so output
  round-trips identically.
- **`reference/validation-rules.md`** — every `LEX`, `PARSE`, and `VAL`
  diagnostic with meaning and severity. Read this to avoid emitting invalid DSL.
- **`examples/`** — complete, valid, canonically-formatted `.gravity` files.

## File structure (top to bottom)

A file is parsed in this order. Every part is optional — even an empty file
parses (it produces a `SourceFile` with no declarations).

```gravity
namespace hr;                      // 0 or 1, must be first if present

import "common/contact.gravity";   // 0 or more, after namespace
import "Employee.gravity";

// then 0 or more top-level declarations: entity | type | enum
type ContactInfo { email: String; phone: String?; }
enum ContractType { CDI, CDD, Freelance, Intern }
entity Employee version 1 { /* ... */ }
```

- `namespace` is a dotted identifier (`a.b.c`), terminated by `;`. At most one,
  and it must precede imports and declarations.
- `import "path";` takes a string literal path; zero or more, before declarations.
- Top-level declarations are `entity`, `type` (value type), and `enum`. They may
  be interleaved in any order. Leading annotations attach to the next declaration.

## Type system

**Primitives** (case-sensitive, exactly these eight):
`String`, `Int`, `Long`, `Decimal`, `Boolean`, `Date`, `DateTime`, `UUID`.

**Modifiers** on a type reference, written as suffixes:

| Suffix | Meaning           | Example        |
|--------|-------------------|----------------|
| `?`    | optional/nullable | `String?`      |
| `[]`   | array             | `Money[]`      |
| `?[]`  | optional + array  | `Money?[]`     |
| `@N`   | version-qualified | `Money@2`      |

- `@N` may only appear on a **named** type reference (a value type or enum),
  written **before** `?`/`[]` (`Money@2?[]`). It is **rejected** on primitives,
  on relation targets, and on command `returns` types.
- `?` and `[]` may be written in either order in source, but canonical form is
  always `?[]`.

**Value types** group named fields; **enums** list variants:

```gravity
type ContactInfo {
  email:     String;
  phone:     String?;
  preferred: ContactMethod;
}

enum ContactMethod { Email, Phone, None }
```

Both `type` and `enum` may carry an optional `version N` (default `1`).

## Entity declaration

```gravity
entity TimeEntry version 1 {

  identity id: UUID;                              // REQUIRED — exactly one

  relations {                                     // optional
    employee:  Employee  cardinality one;
    approver:  Employee? cardinality one  semantic approved_by;
  }

  properties {                                    // optional
    date:        Date;
    hours:       Decimal;
    description: String?;
  }

  lifecycle {                                     // optional
    states { Draft, Submitted, Approved; }
    transitions {
      Draft     -> Submitted on Submitted;
      Submitted -> Approved  on Approved;
    }
  }

  events {                                        // optional
    Submitted { submitted_at: DateTime; };
    Approved  { approver_id: UUID; approved_at: DateTime; };
  }

  commands {                                      // optional
    Submit()
      returns SubmissionResult
      with side_effect Submitted;
    Approve(approver_id: UUID)
      returns ApprovalResult
      with side_effect Approved;
  }
}
```

**Section rules**

- `identity <field>: <Type>;` is **required**; exactly one per entity. The type
  should be `UUID` (any other type is a `VAL005` warning, not an error).
- Every other section is optional. A section may appear **at most once**
  (a duplicate is `PARSE002`).
- In **source** the six sections may appear in any order. In **canonical output**
  they are always emitted in this fixed order:
  `identity → relations → properties → lifecycle → events → commands`.
- `relations`: `name: Target[?] cardinality one|many [semantic ident];`
  - `?` + `cardinality many` is forbidden (`VAL010`). Use `cardinality many`
    with no `?` for an empty-collection relation.
  - `[]` is not allowed on a relation target (`PARSE005`) — use `cardinality many`.
- `lifecycle.states { A, B, C; }` — comma-separated identifiers, optional trailing
  `;` before `}`, trailing comma tolerated.
- `lifecycle.transitions { From -> To on Event; }` — every `From`/`To` must be a
  declared state (`VAL001`) and every `Event` must be a declared event (`VAL002`).
- `events`: `Name { field: Type; ... };` — note the **trailing semicolon** after
  the closing brace. Empty payload is written `Name {};`.
- `commands`: `Name(arg: Type, ...) returns ResultType with side_effect Event;`
  - The `side_effect` event must be a declared event (`VAL003`).
  - `returns` names a value type by bare name (no `@N`).

## Versioning & deprecation

```gravity
entity Employee version 2 deprecates version 1 until "2026-12-31" {
  // ...
}
```

- Every entity declares `version N` (a positive integer). `type`/`enum` may too.
- Additive changes within a chain are fine. **Breaking** changes (field removal,
  type narrowing, lifecycle-state removal, command/event removal, command-arg
  breaking changes) require a `deprecates version M until "YYYY-MM-DD"` clause,
  else the toolchain refuses them (`VAL020`–`VAL030`).
- The `until` date must be a valid `YYYY-MM-DD` calendar date (`VAL009`).

## Annotations

Target-specific hints use namespaced annotations. The core grammar stays small;
emitters claim namespaces and validate their own keys.

```gravity
@csharp(summary: "Order aggregate")
entity Order version 1 {
  identity id: UUID;
  properties {
    email: String @json_schema(format: "email") @graphql(searchable: true);
  }
}
```

- Form: `@namespace` or `@namespace.name`, optionally `(key: value, ...)`.
- **Argument keys must be plain identifiers** — reserved words are not allowed.
  `@csharp(namespace: "...")` is a **parse error** because `namespace` is a
  keyword; pick a non-reserved key. Keys `true`/`false` are likewise illegal.
- Values are string, integer, decimal, `true`/`false`, or a bare identifier.
  Duplicate keys in one annotation are `PARSE008`.
- A namespace not claimed by a **registered** emitter is `VAL006`. With the stock
  CLI only `csharp` is registered; `json_schema`, `postgres`, and any other
  namespace require their emitter assembly via `--plugin` (or the matching
  `.gravity.yaml` config). So `@json_schema` above validates only when the
  JSON-Schema emitter is loaded; `@graphql` is purely illustrative — no GraphQL
  emitter ships in this repo, so that namespace would always be `VAL006` here.
- Annotation placement that the parser accepts: **leading** on top-level decls
  (`entity`/`type`/`enum`) and on `relations` entries; **trailing** on
  `properties` entries (after the type, before `;`). Event/command annotations
  are not parsed from source in v1.

## Authoring workflow (how to write perfectly)

1. Decide namespace and imports.
2. Declare shared value types / enums / command-result types first.
3. Declare entities; always start the body with `identity`.
4. Keep section order canonical (see above) so output matches `SourceWriter`.
5. Apply the formatting rules in `reference/canonical-format.md` (4-space indent,
   LF, one blank line between sections, `};` on events, etc.).
6. Run the **pre-flight checklist** in `reference/validation-rules.md` before
   claiming the file is valid.
7. If a real compiler is available, verify with the CLI (e.g.
   `dotnet run --project Gravity.Dsl.Cli -- gen --input <dir> --output <out>`),
   which lexes, parses, resolves, and validates.
