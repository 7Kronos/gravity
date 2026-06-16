# Gravity DSL — canonical formatting

The reference compiler's `SourceWriter` (`AST → .gravity`) emits one canonical
form. The round-trip guarantee is: *parse → serialize → re-parse yields the same
AST*, and serialization is byte-identical for identical input. Write source that
already matches this form and it round-trips untouched.

Transcribed from `Gravity.Dsl.Compiler/Parsing/SourceWriter.cs`.

## Whole-file rules

1. **Indent** = 4 spaces per level. Never tabs.
2. **Line ending** = `\n` (LF) only. No `\r`.
3. **No trailing whitespace** on any line.
4. **Exactly one trailing newline** at end of file (no blank lines at EOF).
5. **Exactly one blank line** between consecutive top-level declarations, and
   between sections inside an entity. Never two blank lines.
6. **Comments are dropped.** The serializer never emits `//` or `/* */`.

## File header order

```
namespace <name>;
                          <- blank line if imports or decls follow
import "<path>";          <- each import on its own line, no blank between them
import "<path>";
                          <- blank line before first declaration
<decl>
                          <- blank line between declarations
<decl>
```

## Declarations

### Value type

```
type <Name> {
    <field>: <Type>;
    <field>: <Type>;
}
```

- `version N` is printed **only when N ≠ 1** (`type Money version 2 { ... }`).
- Each field indented one level, `name: Type;`.

### Enum

```
enum <Name> { ... }
```

written multi-line, one variant per line, comma after every variant except the
last, no trailing comma:

```
enum ContractType {
    CDI,
    CDD,
    Freelance,
    Intern
}
```

- `version N` printed only when `N ≠ 1`.

### Entity

```
entity <Name> version <N> {
    identity <field>: <Type>;

    relations {
        ...
    }

    properties {
        ...
    }

    lifecycle {
        ...
    }

    events {
        ...
    }

    commands {
        ...
    }
}
```

- `version N` is **always** printed for entities (even `version 1`).
- `deprecates`: ` deprecates version <M> until "<YYYY-MM-DD>"` immediately after
  the version, before `{`.
- **Section order is fixed**: identity, relations, properties, lifecycle, events,
  commands. Empty sections are **omitted** entirely (and `identity` is always
  present). One blank line separates each emitted section.

### Section bodies

**identity** — single line, one level in:
```
    identity id: UUID;
```

**relations** — each relation two levels in; leading annotations (if any) on
their own lines above:
```
    relations {
        employee: Employee cardinality one;
        approver: Employee? cardinality one semantic approved_by;
    }
```
Note the canonical single spaces: `name: Target[?] cardinality one|many[ semantic x];`
(the aligned multi-space padding seen in hand-authored samples is collapsed).

**properties** — each property two levels in; trailing annotations separated by
single spaces after the type:
```
    properties {
        email: String @json_schema(format: "email");
    }
```

**lifecycle** — `states` and `transitions` always emitted in that order, two
levels in; state list is one line three levels in, comma-space separated, ending
in `;`:
```
    lifecycle {
        states {
            Draft, Submitted, Approved;
        }
        transitions {
            Draft -> Submitted on Submitted;
            Submitted -> Approved on Approved;
        }
    }
```

**events** — each event two levels in. Empty payload is `Name {};` on one line.
Non-empty payload puts each field three levels in, closing brace + `;` two levels
in:
```
    events {
        Resubmitted {};
        Approved {
            approver_id: UUID;
            approved_at: DateTime;
        };
    }
```

**commands** — name + `(args)` on the first line two levels in, then `returns`
and `with side_effect` each on their own line three levels in:
```
    commands {
        Approve(approver_id: UUID)
            returns ApprovalResult
            with side_effect Approved;
    }
```

## Type references

`SourceWriter.WriteTypeRef` renders `Name@N?[]` in that fixed order:

1. the type name (primitive keyword or named ref),
2. `@N` only for named refs that carried a version suffix,
3. `?` if optional,
4. `[]` if array.

So `Money?[]` and a source-written `Money[]?` both canonicalize to `Money?[]`
(`?` always precedes `[]`).

## Annotation rendering

`@namespace[.name][(args)]`. Arguments are emitted **sorted by key in ordinal
order**, `key: value`, comma-space separated. String values are re-escaped
(`\\`, `\"`, `\n`, `\r`, `\t`); booleans as `true`/`false`; ints/decimals in
invariant culture; identifiers bare.

```
@csharp(summary: "Primary contact email")
@json_schema(format: "email", maxLength: 320)
```

(Keys render in ordinal order: `format` < `maxLength`. The namespaces shown ship
with the reference toolchain — `csharp` always, `json_schema` via `--plugin`.)
