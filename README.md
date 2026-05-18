# Gravity DSL

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
