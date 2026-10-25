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
- **Exit codes** - `0` on success, `1` on any failure. Errors are one-line messages naming the
  offending thing.
- **Output routing** - MSBuild diagnostic lines (the shape the SDK's IL3000-family analyzer
  warnings arrive in) and all stderr lines surface as warnings so they survive `--quiet`.

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
releasegen build [--preset <name>]
```

| Flag | Effect |
|------|--------|
| `--preset <name>` | Overrides the Manifest's `build.preset` for this invocation only. Never writes the Manifest. |

A `tar.xz` entry never produces an archive for a Windows-class (`win-*`) or portable (`any-*`) rid.
When `tar.xz` is declared alongside another format, the stage skips it for each such rid and
prints one warning per skipped (target, rid) pair:

```
warning: build: target '<project>' with runtime identifier '<rid>' skips archive format 'tar.xz': tar.xz cannot be produced for Windows-class or portable (any-*) runtime identifiers.
```

A target whose only declared format is `tar.xz` fails manifest validation before any publish
runs when the Manifest declares those rids: the error names the target, the conflicting rids,
and the fix - see the [manifest schema](manifest-schema.md).

## `test`

Runs `dotnet test` on the projects the Manifest's test section names, or once from the current
directory when the section lists none. A declared section that exits nonzero fails the
stage, so a chained run aborts before publish and the test gate blocks publish.
A Manifest with no test section warns and exits 0 - the test gate passes by default. Named
projects run fail-fast: the first nonzero exit stops the stage.

```
releasegen test
```

No stage flags in v1.

## `package`

Runs `dotnet pack -c Release -o <output>` once per declared package kind per target, producing
exactly the artifact kinds each target declares and nothing more. No inference from
project file type: a kind is produced because the Manifest declares it, never because a csproj
looks like one. Output lands under `release/packages/lib/<project-name>/` for `lib-package` and
`release/packages/tool/<project-name>/` for `tool-package`. The `executables` kind is the build
stage's product and is skipped here, never failed on.

```
releasegen package
```

No stage flags in v1.

## `publish`

Pushes each target's declared package kinds with `dotnet nuget push` in Manifest target order to the resolved sources: the Manifest's `publish.sources`, or nuget.org when the list
is empty. Before the first push the stage consults the test gate: a blocked
outcome stops the stage before any network call, and an absent test section proceeds with a
warning. A standalone `publish` consults the gate by running the test stage itself, so the
gate cannot be bypassed by separate invocations. The full push list is collected before the
first push, so a missing package fails the stage with no partial upload.

```
releasegen publish [--api-key <key>]
```

| Flag | Effect |
|------|--------|
| `--api-key <key>` | The NuGet API key. Falls back to the `NUGET_API_KEY` environment variable. The Manifest is never consulted for keys - no key ever lives on disk. |

The key is redacted from console output. In CI, prefer trusted publishing; see
[trusted publishing](trusted-publishing.md).

## `run`

The chained run: executes the stages the Manifest names in Manifest order -
`build`, `test`, `package`, `publish` - aborting at the first failed stage and reporting each
skipped unnamed stage with a console note. Stage selection follows the Manifest: build always
runs; test is named by a declared test section; package and publish are named by targets
declaring package kinds. The test stage's gate outcome is carried to publish within the chain,
so tests never run twice.

```
releasegen run [--preset <name>]
```

| Flag | Effect |
|------|--------|
| `--preset <name>` | Overrides the Manifest's `build.preset` for this invocation only, forwarded to the build stage. Never writes the Manifest. |

## See also

- [Manifest schema](manifest-schema.md)
- [Build presets](presets.md)
- [Stage handoff contract](stage-handoff.md) - the artifact-location story behind separately
  invoked stages
