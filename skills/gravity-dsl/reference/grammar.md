# Gravity DSL — grammar reference

Transcribed from `Gravity.Dsl.Compiler/Lexing/{TokenKind,Lexer}.cs` and
`Gravity.Dsl.Compiler/Parsing/Parser.cs`. This is the v1 grammar.

## Lexical structure

### Trivia (skipped, never tokens)

- **Whitespace**: space, tab, `\r`, `\n`. Line/column are tracked for diagnostics.
- **Line comment**: `// ... ` to end of line.
- **Block comment**: `/* ... */`, may span lines. Unterminated → `LEX001`.
- Comments are **dropped** by the canonical serializer — they do not round-trip.

### Identifiers and reserved words

- Identifier: `[A-Za-z_][A-Za-z0-9_]*` (ASCII only; `_` allowed anywhere).
- An identifier matching a reserved word lexes as that keyword.

The **22 reserved words** (`ClassifyIdentifier`):

```
namespace  import   entity   type     enum     version  deprecates  until
identity   relations properties  lifecycle  states  transitions  on
events     commands returns  with     side_effect  cardinality  semantic
```

Plus the two boolean literals used only in annotation values: `true`, `false`.

> `cardinality`'s argument (`one`/`many`) and a relation's `semantic` name are
> ordinary identifiers, not keywords — they are validated by the parser, not the
> lexer.

### Literals

- **Integer**: one or more digits `[0-9]+`. No sign. (A leading `-` only ever
  appears as part of the `->` arrow.)
- **Decimal**: `[0-9]+ '.' [0-9]+` — digits required on **both** sides of the dot.
- **String**: `"..."`. Supported escapes: `\"`, `\\`, `\n`, `\t`, `\r`. Any other
  escape is `LEX002`. Strings do not span lines (newline → unterminated, `LEX001`).

### Punctuation / operators

```
{  }  (  )  [  ]   ;  ,  :  ?   ->   @  .
```

`->` is the only two-character token.

## Grammar (EBNF)

Notation: `?` = optional, `*` = zero+, `+` = one+, `|` = alternation,
`'x'` = literal token, UPPER = lexical class.

```ebnf
SourceFile   = Namespace? Import* TopLevelDecl* ;

Namespace    = 'namespace' DottedName ';' ;
DottedName   = IDENT ( '.' IDENT )* ;

Import       = 'import' STRING ';' ;

TopLevelDecl = Annotation* ( EntityDecl | ValueTypeDecl | EnumDecl ) ;

(* ---------- value type & enum ---------- *)
ValueTypeDecl = 'type' IDENT ( 'version' INT )? '{' Field* '}' ;
EnumDecl      = 'enum' IDENT ( 'version' INT )?
                '{' ( IDENT ( ',' IDENT )* ','? )? '}' ;

Field         = IDENT ':' TypeRef ';' ;

(* ---------- entity ---------- *)
EntityDecl   = 'entity' IDENT 'version' INT Deprecates?
               '{' EntitySection* '}' ;          (* identity section REQUIRED *)
Deprecates   = 'deprecates' 'version' INT 'until' STRING ;

EntitySection = Identity | Relations | Properties
              | Lifecycle | Events | Commands ;   (* each at most once *)

Identity     = 'identity' IDENT ':' TypeRef ';' ;

Relations    = 'relations' '{' Relation* '}' ;
Relation     = Annotation* IDENT ':' IDENT '?'?
               'cardinality' ('one'|'many')
               ( 'semantic' IDENT )? ';' ;        (* no @N, no [] on target *)

Properties   = 'properties' '{' Property* '}' ;
Property     = IDENT ':' TypeRef Annotation* ';' ;

Lifecycle    = 'lifecycle' '{' ( States | Transitions )* '}' ;
States       = 'states' '{' ( IDENT ( ',' IDENT )* ','? )? ';'? '}' ;
Transitions  = 'transitions' '{' Transition* '}' ;
Transition   = IDENT '->' IDENT 'on' IDENT ';' ;

Events       = 'events' '{' Event* '}' ;
Event        = IDENT '{' Field* '}' ';' ;          (* note trailing ';' *)

Commands     = 'commands' '{' Command* '}' ;
Command      = IDENT '(' ( Arg ( ',' Arg )* )? ')'
               'returns' IDENT                      (* bare name, no @N *)
               'with' 'side_effect' IDENT ';' ;
Arg          = IDENT ':' TypeRef ;                  (* no trailing ';' *)

(* ---------- types ---------- *)
TypeRef      = IDENT VersionSuffix? TypeMods ;
VersionSuffix= '@' INT ;                            (* named refs only *)
TypeMods     = ( '?' ( '[' ']' )? ) | ( '[' ']' '?'? ) | (* nothing *) ;

(* ---------- annotations ---------- *)
Annotation   = '@' IDENT ( '.' IDENT )? ( '(' AnnArg ( ',' AnnArg )* ')' )? ;
AnnArg       = IDENT ':' AnnValue ;
AnnValue     = STRING | INT | DECIMAL | 'true' | 'false' | IDENT ;
```

## The `@N` version suffix — exact rules

`@N` is parsed on a `TypeRef` **before** the `?`/`[]` modifiers, with these
constraints (`Parser.ParseTypeRef`, `RefuseVersionSuffix`):

- `N` must be a positive integer **immediately adjacent** to `@` (no space:
  `Money@2`, not `Money@ 2`), with **no leading zero**. Violations → `PARSE020`.
- Allowed only on **named** type references (value types/enums) — i.e. in
  `identity`, `properties`, value-type/event `Field`s, and command `Arg`s.
- **Rejected** on primitives, on relation targets, and on command `returns`
  types (each → `PARSE020`).
- Disambiguation: `@` followed by an *identifier* is an **annotation**
  (`@csharp(...)`), not a version suffix. `@` followed by a digit/other is a
  version-suffix attempt.

## Things the parser enforces structurally

- An entity with no `identity` section → `PARSE004`.
- A duplicate section inside an entity → `PARSE002`.
- An unexpected token where a section keyword or `}` is expected → `PARSE003`.
- `cardinality` must be `one` or `many` → otherwise `PARSE006`.
- A `[]` on a relation target → `PARSE005`.
- Top level expects `entity`/`type`/`enum` → otherwise `PARSE001`.
- Duplicate annotation argument key → `PARSE008`.
- Maximum nesting depth exceeded → `PARSE010`.

See `reference/validation-rules.md` for the full diagnostic catalog and the
semantic (`VAL`) rules that run after a successful parse.
