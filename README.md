# Gravity DSL

[![CI](https://github.com/7Kronos/gravity/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/7Kronos/gravity/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

Gravity DSL is a textual definition language for AI-constrained enterprise software
development. It declares domain entities — identity, relations, properties, lifecycle,
events, and commands — in a precise, versioned, AI-readable form, and drives
multi-target code generation through a pluggable emitter architecture.

The v1 proposal lives in [`docs/specs.md`](docs/specs.md). The locked specification
for Phases 0–3 (spike, compiler core, AST publication and emitter host, C# reference
emitter) is in [`specs/001-gravity-dsl/spec.md`](specs/001-gravity-dsl/spec.md), with
its implementation plan and task list as sibling files.

The build is not yet wired end-to-end; this repository is in active Phase 0 scaffolding.

## The `gravc` CLI

`gravc` is the command-line compiler: it validates (`gravc check`) and generates
code (`gravc gen`) from `.gravity` sources. It is distributed two ways:

- **Standalone single-file binaries** (self-contained — no .NET runtime
  required), attached to each
  [GitHub Release](https://github.com/7Kronos/gravity/releases) for Linux,
  macOS, and Windows (x64 + arm64).
- **dotnet global tool**: `dotnet tool install -g gravc` (smaller download when
  you already have the .NET SDK).

Both distributions are functionally identical, including external `--plugin`
emitter support.

Install and usage instructions live in the `gravc-cli` skill —
[`skills/gravc-cli/SKILL.md`](skills/gravc-cli/SKILL.md).

## Agent skills

This repo ships reusable agent skills (installable into Claude Code as a plugin
or plain skill, or into OpenAI Codex):

- [`skills/gravity-dsl/`](skills/gravity-dsl/) — read and write `.gravity` source
  correctly: full grammar, canonical formatting, diagnostics, validated examples.
- [`skills/gravc-cli/`](skills/gravc-cli/) — install and drive the `gravc` CLI.

See [`skills/gravity-dsl/README.md`](skills/gravity-dsl/README.md) for the full
install matrix. Quick start for Claude Code (the plugin bundles both skills):

```
/plugin marketplace add 7Kronos/gravity
/plugin install gravity-dsl@gravity
```
