# Command reference

ReleaseGen ships six commands: five stage commands plus the chained run. The tool
command name is `releasegen`. Every command shares the global `--quiet` option, and bare
`releasegen` prints command help - no stage executes without an explicit verb.

## Global behavior

- **Manifest discovery** - every stage command (except `init`) discovers `releasegen.json` by
  walking up from the current directory to the repository root, stopping at the first manifest
  found. When none is found, the error names every searched directory.
- **`--quiet`** - suppresses stage notes and progress while warnings and errors stay visible.
  It is the only verbosity switch in v1; there is no `--verbose`.
- **`--dry-run`** - accepted by every stage command and the chained run (not by `init`). The
  stage prints its would-be actions and writes nothing anywhere; see [Dry run](#dry-run).
- **Exit codes** - `0` on success, `1` on any failure. Errors are one-line messages naming the
  offending thing.
- **Output routing** - MSBuild diagnostic lines (the shape the SDK's IL3000-family analyzer
  warnings arrive in) and all stderr lines surface as warnings so they survive `--quiet`.

## Dry run

`--dry-run` turns any stage command, or the chained run, into a rehearsal: the stage runs its
full planning and read-only steps and prints what a real run would have done, stage by stage,
while every write and outward side effect is suppressed. It composes with `--quiet`.

- **Zero writes** - nothing is written anywhere, including the `release/` tree (no stale-output
  cleanup, no publish folders, no archives, no packages) and `TestResults/`. Archives and
  package contents stay invisible: the report describes them instead of producing them.
- **One plan, both modes** - each spawned command appears as the exact command line a real run
  would execute. Side effects a real run guards at run time (for example archiving a publish
  output folder) print as `(<command line>) (conditional: <guard>)`, so the report may describe
  work a real run would later skip.
- **Publish prints the gate consult** - instead of running tests, publish prints the would-be
  gate consult (`tests would run for: …`) naming the test projects a real run's gate would
  execute, then the would-be pushes with the API key redacted. A dry run needs no API key.
- **Exit code = manifest validation only** - a valid manifest exits `0`; a manifest that fails
  validation (or an unknown preset) exits `1`. No other condition can fail a dry run.
- **A plan, not a certification** - a zero-exit dry run proves the manifest is valid and the
  plan reads as expected. It is not a test result and not a release: run the stages for real
  before pushing anything.

```
releasegen build --dry-run
releasegen run --dry-run
```

## `init`

Writes a minimal valid `releasegen.json` Manifest to the current directory. Init is a
one-shot command and takes no part in the chained run.

```
releasegen init [--preset <name>] [--overwrite]
```

| Flag | Effect |
|------|--------|
| `--preset <name>` | Seeds the Manifest's `build.preset` key with a known preset name. `none`, or omitting the flag, seeds no key (absence means preset none). An unknown name fails with an error listing the known set. |
| `--overwrite` | Replace `releasegen.json` when it already exists in the current directory. Without it, an existing file is refused with exit code 1. |

## `build`

Builds release binaries: runs `dotnet publish -c Release -r <rid>` once per target per declared
runtime identifier, writing each (target, rid) publish to `release/<rid>/<project-name>/`, and
attaching one archive per (target, rid) for each resolved format (`release/<project-name>-<rid>.zip`,
`.tar.gz`, or `.tar.xz`). With no `archiveFormats` key the default set resolves per rid class:
zip for Windows-class and portable `any-*` rids, zip plus tar.gz for Unix-based rids.
`tar.xz` is opt-in and never a default. Output stays local - nothing is uploaded.

```
releasegen build [--preset <name>] [--dry-run]
```

| Flag | Effect |
|------|--------|
| `--preset <name>` | Overrides the Manifest's `build.preset` for this invocation only. Never writes the Manifest. |
| `--dry-run` | Prints each publish and archive the stage would run, writes nothing under `release/`, and exits on Manifest validation alone. See [Dry run](#dry-run). |

A `tar.xz` entry never produces an archive for a Windows-class (`win-*`) or portable (`any-*`) rid.
When `tar.xz` is declared alongside another format, the stage skips it for each such rid and
prints one warning per skipped (target, rid) pair:

```
warning: build: target '<project>' with runtime identifier '<rid>' skips archive format 'tar.xz': tar.xz cannot be produced for Windows-class or portable (any-*) runtime identifiers.
```

A target whose only declared format is `tar.xz` fails manifest validation before any publish
runs when the Manifest declares those rids: the error names the target, the conflicting rids,
and the fix - see the [manifest schema](manifest-schema.md).

`tar.xz` is written by piping the tar stream through the system `xz` binary, so the machine
running the stage must have `xz` on PATH. A machine without it fails the stage before any
archive is produced, naming the missing tool:

```
error: build: cannot create archive '<release>/<project-name>-<rid>.tar.xz': the 'xz' binary was not found on PATH. Install xz (for example 'apt-get install xz-utils', 'brew install xz', or 'pacman -S xz') to produce 'tar.xz' archives, or declare 'zip' or 'tar.gz' instead.
```

An `xz` run that exits nonzero fails the stage in the same shape, naming the tool and its exit
code; either way nothing half-written is left under `release/`.

## `test`

Runs `dotnet test` on the projects the Manifest's test section names, or once from the current
directory when the section lists none. A declared section that exits nonzero fails the
stage, so a chained run aborts before publish and the test gate blocks publish.
A Manifest with no test section warns and exits 0 - the test gate passes by default. Named
projects run fail-fast: the first nonzero exit stops the stage.

```
releasegen test [--dry-run]
```

| Flag | Effect |
|------|--------|
| `--dry-run` | Prints the `dotnet test` invocation(s) a real run would start, runs no tests, writes no `TestResults/`, and exits on Manifest validation alone. See [Dry run](#dry-run). |

## `package`

Runs `dotnet pack -c Release -o <output>` once per declared package kind per target, producing
exactly the artifact kinds each target declares and nothing more. No inference from
project file type: a kind is produced because the Manifest declares it, never because a csproj
looks like one. Output lands under `release/packages/lib/<project-name>/` for `lib-package` and
`release/packages/tool/<project-name>/` for `tool-package`. The `executables` kind is the build
stage's product and is skipped here, never failed on.

```
releasegen package [--dry-run]
```

| Flag | Effect |
|------|--------|
| `--dry-run` | Prints the `dotnet pack` invocation(s) a real run would start, writes nothing under `release/packages/`, and exits on Manifest validation alone. See [Dry run](#dry-run). |

## `publish`

Pushes each target's declared package kinds with `dotnet nuget push` in Manifest target order to the resolved sources: the Manifest's `publish.sources`, or nuget.org when the list
is empty. Before the first push the stage consults the test gate: a blocked
outcome stops the stage before any network call, and an absent test section proceeds with a
warning. A standalone `publish` consults the gate by running the test stage itself, so the
gate cannot be bypassed by separate invocations. The full push list is collected before the
first push, so a missing package fails the stage with no partial upload.

```
releasegen publish [--api-key <key>] [--dry-run]
```

| Flag | Effect |
|------|--------|
| `--api-key <key>` | The NuGet API key. Falls back to the `NUGET_API_KEY` environment variable. The Manifest is never consulted for keys - no key ever lives on disk. |
| `--dry-run` | Prints the would-be gate consult (the test projects the gate would run) and the would-be pushes, runs no tests, writes no `TestResults/`, pushes nothing, and needs no API key. See [Dry run](#dry-run). |

The key is redacted from console output. In CI, prefer trusted publishing; see
[trusted publishing](trusted-publishing.md).

## `run`

The chained run: executes the stages the Manifest names in Manifest order -
`build`, `test`, `package`, `publish` - aborting at the first failed stage and reporting each
skipped unnamed stage with a console note. Stage selection follows the Manifest: build always
runs; test is named by a declared test section; package and publish are named by targets
declaring package kinds. The test stage's gate outcome is carried to publish within the chain,
so tests never run twice. Under `--dry-run` the chain forwards the flag to every named stage,
so the report walks the whole pipeline stage by stage and writes nothing.

```
releasegen run [--preset <name>] [--dry-run]
```

| Flag | Effect |
|------|--------|
| `--preset <name>` | Overrides the Manifest's `build.preset` for this invocation only, forwarded to the build stage. Never writes the Manifest. |
| `--dry-run` | Forwards to every named stage: the chain prints each stage's would-be actions and writes nothing anywhere, exiting on Manifest validation alone. See [Dry run](#dry-run). |

## See also

- [Manifest schema](manifest-schema.md)
- [Build presets](presets.md)
- [Stage handoff contract](stage-handoff.md) - the artifact-location story behind separately
  invoked stages
