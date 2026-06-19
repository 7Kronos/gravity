# Gravity DSL — diagnostics & validation rules

The compiler runs in stages: **lex → parse → resolve → validate**. Each stage can
emit diagnostics with a stable rule id, a `path:line:col` span, and a severity
(`Error` blocks compilation; `Warning` does not). Catalog transcribed from
`Lexer.cs`, `Parser.cs`, `Validation/RuleIds.cs`, and `Validation/Validator.cs`.

## Lexical (`LEX`)

| Id     | Severity | Meaning |
|--------|----------|---------|
| LEX001 | Error    | Unexpected character, unterminated string literal, or unterminated block comment. |
| LEX002 | Error    | Unknown string escape sequence (only `\"` `\\` `\n` `\t` `\r` are valid). |

## Parse (`PARSE`)

| Id      | Severity | Meaning |
|---------|----------|---------|
| PARSE000 | Error | Expected a specific token but got another (generic "expected X but got Y"). |
| PARSE001 | Error | Expected `entity`, `type`, or `enum` at top level. |
| PARSE002 | Error | Duplicate section (`identity`/`relations`/`properties`/`lifecycle`/`events`/`commands`) in one entity. |
| PARSE003 | Error | Unexpected token in entity body; expected a section keyword or `}`. |
| PARSE004 | Error | Entity is missing its required `identity` section. |
| PARSE005 | Error | Relation target used `[]`; use `cardinality many` instead. |
| PARSE006 | Error | `cardinality` value is not `one` or `many`. |
| PARSE007 | Error | Expected `states` or `transitions` inside a `lifecycle` body. |
| PARSE008 | Error | Duplicate annotation argument key. |
| PARSE009 | Error | Expected an annotation value (string, integer, decimal, bool, or identifier). |
| PARSE010 | Error | Maximum nesting depth exceeded. |
| PARSE020 | Error | Bad `version` / `@N` suffix: not a positive integer, leading zero, whitespace after `@`, on a primitive, on a relation target, or on a command `returns` type. |
| PARSE021 | Error | Annotation integer value out of `Int64` range. |

## Semantic validation (`VAL`)

### Phase 0–3 rules

| Id     | Severity | Meaning |
|--------|----------|---------|
| VAL001 | Error    | A `transitions` entry names a `From`/`To` state not declared in `states {}`. |
| VAL002 | Error    | A transition's `on` event is not declared in `events {}`. |
| VAL003 | Error    | A command's `side_effect` event is not declared in `events {}`. |
| VAL004 | Warning  | A declared state (other than the first) has no incoming transition. |
| VAL005 | Warning  | The `identity` field type is not `UUID` (UUID recommended). |
| VAL006 | Error    | Annotation namespace is not claimed by any registered emitter. |
| VAL009 | Error    | `deprecates ... until` date is not a well-formed / valid `YYYY-MM-DD`. |
| VAL010 | Error    | A relation combines `?` (optional) with `cardinality many`. |
| VAL031 | Error    | A `Map<K, V>` key type is not a non-optional, non-array scalar primitive. |

> `VAL007` (a namespace claimed by two emitters) is enforced by the emitter host,
> not the core validator.

### Phase 8 — additive-only / breaking-change rules

These compare a higher version against the one it `deprecates`. They are how the
toolchain "refuses breaking changes without a `deprecates` clause + window."

| Id     | Severity | Meaning |
|--------|----------|---------|
| VAL020 | Error    | Field removed (entity property, value-type field, or event payload field). |
| VAL021 | Error    | Type narrowed on a surviving field (e.g. `Long`→`Int`, optional lost, array lost, `DateTime`→`Date`). |
| VAL022 | Error    | A lifecycle state was removed. |
| VAL023 | Error    | A command was removed. |
| VAL024 | Error    | An event was removed. |
| VAL025 | Warning  | A lifecycle transition was removed. |
| VAL026 | Error    | A command argument change is breaking (added required arg, narrowed arg type). |
| VAL027 | Error    | The `deprecates` chain is broken (a skipped link). |
| VAL028 | Error    | `deprecates` names a version that does not exist. |
| VAL029 | Error    | `deprecates` references itself / a forward version. |
| VAL030 | Error    | The deprecation window has expired (the `until` date is in the past). |

## Pre-flight checklist (run before claiming a `.gravity` file is valid)

**Structure**
- [ ] At most one `namespace`, first if present; `import`s before declarations.
- [ ] Every `entity` has exactly one `identity` section.
- [ ] No section repeated within an entity.
- [ ] Identity type is `UUID` (else accept the `VAL005` warning consciously).

**Types**
- [ ] Every primitive is one of the eight exact spellings.
- [ ] `@N` only on named refs, adjacent to `@`, no leading zero, before `?`/`[]`.
- [ ] No `@N` on primitives, relation targets, or command `returns`.
- [ ] No `[]` on a relation target; no `?` + `cardinality many`.

**Lifecycle**
- [ ] Every transition `From`/`To` is a declared state.
- [ ] Every transition `on` event is a declared event.
- [ ] Every command `side_effect` is a declared event.
- [ ] Every reachable state has an incoming transition (else `VAL004` warning).

**Syntax punctuation**
- [ ] Each event is `Name { ... };` — trailing `;` after `}` (or `Name {};`).
- [ ] Command args have no trailing `;`; the command ends `... side_effect E;`.
- [ ] Fields and the state list end with `;`.

**Annotations**
- [ ] Namespaces are claimed by an emitter; no duplicate argument keys.

**Versioning**
- [ ] Breaking changes carry a `deprecates version M until "YYYY-MM-DD"` with a
      valid, future date and an unbroken chain.

**Formatting** (see `canonical-format.md`)
- [ ] 4-space indent, LF, one blank line between sections/decls, single trailing
      newline, no trailing whitespace.
