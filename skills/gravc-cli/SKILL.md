---
name: gravc-cli
description: >-
  Install and use the gravc CLI — the Gravity DSL compiler that validates
  (.gravity check) and generates code (.gravity gen) from .gravity sources. Use
  when you need to install gravc from its GitHub release (standalone
  single-file binary) or as a dotnet global tool, or when running gravc
  check/gen, choosing emitters, the --as-of date, or external --plugin emitters.
---

# gravc CLI — install & use

`gravc` is the command-line compiler for the Gravity DSL. It lexes, parses,
resolves, and validates `.gravity` sources and runs the reference emitters to
generate code. Two distributions:

| Distribution | Best for | Needs .NET installed? |
|---|---|---|
| **Standalone single-file binary** (GitHub release) | zero-dependency use, CI, machines without .NET | No — bundles the runtime |
| **dotnet global tool** (`gravc` on NuGet) | smaller download when you already have the SDK | Yes (.NET 9) |

Both distributions are functionally identical: validation, the built-in C#
emitter, and external `--plugin` emitters all work. The standalone binary just
bundles the .NET runtime so nothing else needs to be installed.

---

## Install — standalone binary (GitHub releases)

Release assets are named `gravc-<version>-<rid>.<ext>` with a matching
`.sha256`, where `<rid>` is one of:

| OS / arch | RID | archive |
|---|---|---|
| Linux x64 | `linux-x64` | `.tar.gz` |
| Linux arm64 | `linux-arm64` | `.tar.gz` |
| Windows x64 | `win-x64` | `.zip` |
| macOS Intel | `osx-x64` | `.tar.gz` |
| macOS Apple Silicon | `osx-arm64` | `.tar.gz` |

### Linux / macOS (bash)

```bash
REPO=7Kronos/gravity
# Detect RID
os=$(uname -s); arch=$(uname -m)
case "$os" in Linux) o=linux ;; Darwin) o=osx ;; *) echo "unsupported OS: $os" >&2; exit 1 ;; esac
case "$arch" in x86_64|amd64) a=x64 ;; aarch64|arm64) a=arm64 ;; *) echo "unsupported arch: $arch" >&2; exit 1 ;; esac
rid="$o-$a"

# Resolve latest version tag (or set VERSION=1.2.3 to pin)
VERSION=${VERSION:-$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
  | grep -oE '"tag_name":\s*"[^"]+"' | head -1 | sed -E 's/.*"v?([^"]+)".*/\1/')}

asset="gravc-${VERSION}-${rid}.tar.gz"
base="https://github.com/$REPO/releases/download/v${VERSION}"
curl -fsSL -O "$base/$asset"
curl -fsSL -O "$base/$asset.sha256"

# Verify checksum
if command -v sha256sum >/dev/null 2>&1; then sha256sum -c "$asset.sha256"; else shasum -a 256 -c "$asset.sha256"; fi

# Extract and install to ~/.local/bin (must be on PATH)
mkdir -p ~/.local/bin
tar -xzf "$asset" -C ~/.local/bin gravc
chmod +x ~/.local/bin/gravc
gravc --help
```

> macOS Gatekeeper may quarantine a downloaded binary. If it refuses to run:
> `xattr -d com.apple.quarantine ~/.local/bin/gravc`.

### Windows (PowerShell)

```powershell
$Repo = "7Kronos/gravity"
$Version = $env:VERSION
if (-not $Version) {
  $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
  $Version = $rel.tag_name.TrimStart("v")
}
$asset = "gravc-$Version-win-x64.zip"
$base  = "https://github.com/$Repo/releases/download/v$Version"
Invoke-WebRequest "$base/$asset" -OutFile $asset
Invoke-WebRequest "$base/$asset.sha256" -OutFile "$asset.sha256"

# Verify checksum
$expected = (Get-Content "$asset.sha256").Split(" ")[0]
if ((Get-FileHash $asset -Algorithm SHA256).Hash -ine $expected) { throw "checksum mismatch" }

$dest = "$env:LOCALAPPDATA\Programs\gravc"
Expand-Archive $asset -DestinationPath $dest -Force
# Add $dest to PATH (user scope) if not already present, then:
& "$dest\gravc.exe" --help
```

`gh` CLI alternative (any OS): `gh release download v<version> -R 7Kronos/gravity -p 'gravc-*-<rid>.*'`.

---

## Install — dotnet global tool (full plugin support)

Requires the .NET SDK/runtime (`net9.0`).

```bash
dotnet tool install -g gravc
# update later:
dotnet tool update -g gravc
gravc --help
```

Ensure `~/.dotnet/tools` is on your PATH.

---

## Using gravc

```text
gravc check --input <dir> [--plugin <path>]* [--as-of YYYY-MM-DD]
gravc gen   --input <dir> --output <dir> [--emitter <name>]* [--plugin <path>]* [--as-of YYYY-MM-DD]
```

- **`check`** — lex/parse/resolve/validate every `.gravity` under `--input`.
  Prints diagnostics; emits nothing.
- **`gen`** — everything `check` does, then runs emitters into `--output`.
- **`--input <dir>`** — directory scanned recursively for `*.gravity` (required).
- **`--output <dir>`** — destination for generated artifacts (`gen` only).
- **`--emitter <name>`** — restrict to named emitter(s); repeatable. Omit to run
  all registered emitters. The built-in target is `csharp`.
- **`--as-of YYYY-MM-DD`** — date used to evaluate deprecation windows
  (defaults to today). Makes builds reproducible.
- **`--plugin <path>`** — load external emitter assembly(ies); `<path>` is a
  `.dll` or a directory of them; repeatable. Works in both distributions.

**Exit status:** `0` on success; non-zero when validation fails or arguments are
invalid. Diagnostics are written to stderr (errors) / stdout (warnings), each as
`path:line:col: <severity> <RULEID>: <message>`.

### Examples

```bash
# Validate a registry of definitions
gravc check --input ./registry

# Generate C# into ./gen, pinned to a date for reproducibility
gravc gen --input ./registry --output ./gen --emitter csharp --as-of 2026-01-01

# Use an external emitter
gravc gen --input ./registry --output ./gen --plugin ./emitters/MyEmitter.dll
```

## Verify the install

```bash
gravc --help        # prints usage
gravc check --input <any dir with .gravity files>   # exit 0 == valid
```

If `gravc check` exits 0 on known-good sources, the CLI is working. Pair this
with the [`gravity-dsl`](../gravity-dsl/SKILL.md) skill to author the `.gravity`
files `gravc` consumes.
