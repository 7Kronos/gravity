# gravity-dsl skill

A reusable agent skill for reading and writing **Gravity DSL** (`.gravity`)
source. It carries the full grammar, the byte-exact canonical formatting rules,
the complete diagnostic catalog, and worked examples — so an AI agent can author
and review `.gravity` files that parse, validate, and round-trip against the
reference compiler.

The skill itself is [`SKILL.md`](SKILL.md), with detail in
[`reference/`](reference/) and [`examples/`](examples/).

This page is for **consumers** who want to install the skill into their own
agent toolchain. Pick the section for your tool.

---

## Claude Code

You have two ways to install it. The plugin route is best if you want updates
via a marketplace; the skill-folder route is the quickest for local use.

### Option 1 — Install as a plugin (via marketplace)

This repo ships the plugin manifests (`.claude-plugin/plugin.json` and
`.claude-plugin/marketplace.json`), so it is installable directly from GitHub.

In an interactive Claude Code session:

```
/plugin marketplace add 7Kronos/gravity
/plugin install gravity-dsl@gravity
```

Or non-interactively from your shell:

```bash
claude plugin marketplace add 7Kronos/gravity
claude plugin install gravity-dsl@gravity
```

`gravity-dsl` is the plugin name; `gravity` is the marketplace name (the
`name` field in `marketplace.json`). After installing, reload if needed:

```
/reload-plugins
```

**Invoke** the bundled skill (plugin skills are namespaced `plugin:skill`):

```
/gravity-dsl:gravity-dsl
```

Claude will also load it automatically when a task matches the skill's
`description` (authoring, editing, or reviewing `.gravity` files).

### Option 2 — Install as a plain skill (no plugin)

Copy the skill folder into a directory Claude Code auto-discovers. Personal
(all your projects):

```bash
mkdir -p ~/.claude/skills
cp -R skills/gravity-dsl ~/.claude/skills/gravity-dsl
```

Or project-scoped (this repo / a checkout only — committable, shared with the
team):

```bash
mkdir -p .claude/skills
cp -R skills/gravity-dsl .claude/skills/gravity-dsl
```

`SKILL.md` must sit at the folder root (`~/.claude/skills/gravity-dsl/SKILL.md`).
The command name comes from the folder name.

**Verify & invoke:**

```
/skills          # lists discovered skills — gravity-dsl should appear
/gravity-dsl     # invoke it directly
```

---

## OpenAI Codex

Codex treats a folder containing a `SKILL.md` as a first-class **skill**, so the
same folder works without modification. (Codex custom *prompts* are deprecated —
use the skill mechanism below.)

Copy the folder into a Codex skills directory. Global (all projects):

```bash
mkdir -p ~/.agents/skills
cp -R skills/gravity-dsl ~/.agents/skills/gravity-dsl
```

Or project-scoped (from your repo root):

```bash
mkdir -p .agents/skills
cp -R skills/gravity-dsl .agents/skills/gravity-dsl
```

> **Version note.** Current Codex releases discover skills under
> `.agents/skills` (project) and `~/.agents/skills` (global). Some earlier
> builds used `~/.codex/skills` (project: `.codex/skills`) and a few required
> launching with skills enabled. If `/skills` does not list `gravity-dsl` after
> copying, run `/skills` once — it prints the exact path each skill resolved
> from — and place the folder there instead.

**Verify & invoke:**

```
/skills              # lists discovered skills and their resolved paths
$gravity-dsl         # invoke it explicitly in a prompt
```

Codex will also select it implicitly when your prompt matches the skill's
`description`.

---

## What you get once installed

Ask your agent to author or review a `.gravity` file. The skill supplies:

- the full grammar (EBNF, reserved words, lexical rules, `@N` version suffixes),
- the canonical formatting the reference compiler emits (so output round-trips
  byte-for-byte),
- every `LEX`/`PARSE`/`VAL` diagnostic plus a pre-flight checklist,
- three validated, canonically-formatted example `.gravity` files.

To validate the plugin manifests before publishing a fork, run from the repo
root:

```bash
claude plugin validate .
```
