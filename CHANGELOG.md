# Changelog
Notable changes to ReleaseGen, newest first. Package versions come from the static
`<Version>` property in each packable `.csproj`, bumped by hand before each release (see AGENTS.md) - git tags do not drive them.

## 0.4.0 - 2026-11-09

### Added
- **NuGet API key via environment on SDK 10.0.300+**: `publish` probes the installed SDK (`dotnet --version`) and, on 10.0.300 or newer (NuGet 7.6), delivers the API key to each spawned `dotnet nuget push` through the `NUGET_API_KEY` environment variable, so it never appears on the command line or in the process listing. On older SDKs - or when the probe cannot answer - it falls back to `--api-key`, which every SDK accepts. The probe runs only after every refused condition (missing key, blocked test gate, empty push list), so a stage that stops early never spawns it. Printed push lines name the channel instead of an argument and never show the key value in either mode. `PlannedCommand` gains an optional `EnvironmentVariables` dictionary, applied by the runner through the init-only initializer so a null value keeps the inherited environment.
- **`dotnet-releaser` migration skill**: `skills/dotnet-releaser-to-releasegen-migration/` guides migrating a dotnet-releaser (TOML) pipeline to ReleaseGen - TOML config inventory, manifest translation, a coverage report, a dry-run validation gate, and a CI rewrit - backed by a mapping table that classifies each dotnet-releaser surface as Mapped Manual action required, or No ReleaseGen equivalent.
  - **`releasegen build --rids <rids>`**: a comma-separated subset of the Manifest's `runtimeIdentifiers` for this invocation only (for example `--rids win-x64,win-arm64`), the same escape posture as `--preset` - the flag never writes the Manifest, and it can only narrow what the Manifest declares, never add a rid the Manifest does not name. Entries match case-insensitively, de-duplicate, and output paths use the Manifest's spelling. An empty value, a blank entry (naming its position), or a value outside the Manifest's set is refused with an error naming the value and the Manifest's set, exit 1, before any publish is planned. Combined with a per-OS CI matrix (Windows for `win-*`, Linux for `linux-*`, macOS for `osx-*`) it builds a self-contained NativeAOT rid matrix that one runner cannot.

### Changed
- Dependencies: Meziantou.Analyzer to 3.0.320.
- Documentation for commands, stage handoff, and trusted publishing now describes how the API key reaches the spawned push (environment on SDK 10.0.300+, `--api-key` argument otherwise).

## 0.3.0 - 2026-11-04

### Added
- **`--skip-duplicate` on `publish`**: when skipping is enabled, the flag is passed to every `dotnet nuget push`. ReleaseGen performs no pre-check - the package source alone decides what counts as a duplicate. A push that exits 0 with the skip marker in its output is counted as skipped instead of pushed: the marker line is reported as a stage note naming the package and the source, and the closing summary gives pushed and skipped as two totals while the stage still exits 0. A push that exits nonzero still fails the stage as before. A `--dry-run` plan shows the resolved skip mode once per source (only for manifests with package kinds to publish) along with the flag in the planned command line.
- **`skipDuplicates` manifest key**: the publish section gains a nullable bool, so a manifest can state its standing answer for duplicate pushes. The key is matched with exact casing under the strict parse, so a wrong-cased key fails naming the property, and an absent key keeps today's fail-fast behavior. When the manifest and the command line both state a value and the two differ, `--skip-duplicate` wins and one conflict line goes to standard error, at most once per run; a dry-run plan prints the same line a real run would.
- **`--skip-duplicate` forwarded from the chained run**: `releasegen run` accepts the same flag and passes it to publish, so a chained run and a standalone `releasegen publish` resolve one skip decision. The value survives the hop in all three states, so a plain `releasegen run` can never conflict with the manifest: the flag on its own or with `true` turns skipping on, and `--skip-duplicate false` forwards an explicit "off" rather than collapsing into absence.

### Changed
- Dependencies: System.CommandLine to 2.0.14, Microsoft.Extensions.DependencyInjection (and Abstractions) to 10.0.14, Meziantou.Analyzer to 3.0.318.
- Documentation for commands, manifest schema, and the glossary now covers duplicate pushes: the `skipDuplicates` key, the `--skip-duplicate` flag on `publish` and `run`, how the two channels are reconciled into one decision per run, and the pushed/skipped two-total summary.

## 0.2.0 - 2026-11-01

### Breaking changes
- **Build preset rename**: `aot-plus-trimming` is now `aot`. The old name is neither
  aliased nor normalized, so a manifest (or `--preset`) carrying it fails with the usual unknown-preset error, whose known set reads `aot`, `ready-to-run`, `trimming-only`, `framework-dependent`, `none`. Rename the preset before upgrading.
- **Manifest key rename**: each target's `archive` key is now `archiveFormats`. It accepts a string or a list (`"zip"` or `["zip", "tar.gz"]`), both normalizing to one list at parse time, and each format may appear at most once - a duplicate fails validation naming the target and the format. Manifests are parsed strictly, so a leftover `archive` key fails as an unknown field - rename it to `archiveFormats`.
- **Unix archive defaults**: an absent `archiveFormats` key used to mean zip everywhere. It now resolves to zip plus tar.gz for Unix-based rids (`linux-*`, `osx-*`), while Windows-class (`win-*`) and portable (`any-*`) rids keep zip alone. `tar.xz` is never a default on any platform. Declare the key explicitly to pin the previous behavior.

### Added
- **`--dry-run`** on every stage command and on the chained run: full planning with zero writes - nothing under `release/` or `TestResults/`, the publish stage prints the test projects its gate would have run instead of running them, and the exit code reflects manifest validation alone. A zero-exit dry run is a plan, not a release.
- **`--verbose`** on the same commands: each spawned command line with its raw output and a `cause:` line explaining it, reading the same plan the dry-run report prints. Every default (non-verbose) failure gains a `hint: run with --verbose for full detail` line, and passing `--quiet` together with `--verbose` fails with a one-line conflict message rather than silently picking an output mode.
- **`tar.xz` archives**: each target produces one archive per declared format per (target, rid). `tar.xz` is opt-in, is written through the system `xz` binary, and is skipped for Windows-class and portable rids with a warning - or fails validation when no format remains for those rids. A missing `xz` on PATH fails the stage naming the tool to install.
- **`releasegen init --interactive`**: a five-question questionnaire (build preset, projects, runtime identifiers, archive formats, test projects) that writes a tailored manifest, with defaults read from repo detection. It needs a console on stdin; piped input fails with an error pointing at plain `releasegen init`. A lone `tar.xz` answer is refused at the prompt when the chosen rids include rids tar.xz cannot be produced for.
- **Detection-seeded `init`**: the default non-interactive path scans the repository for .NET projects, seeds executable targets, test projects, and package kinds it can classify with certainty, warns once for each classification it cannot make, and falls back to the minimal skeleton when detection fails. Both init write paths validate the composed manifest through the strict loader before writing, so a validation failure writes nothing; detection skips junctions and symlinked directories, so a link cycle cannot diverge the scan.
- **`tool-package` validation**: a target declaring the `tool-package` kind is checked
  against the project's evaluated `PackAsTool` property and fails the build naming the csproj fix when it is not `true`.

### Changed
- `init` writes a richer skeleton: executable targets carry an explicit `archiveFormats` list (`zip`, `tar.gz`) so the default is visible in the file, and a successful write ends with a note covering next steps, single-file rules, and archive defaults.
- Documentation for commands, manifest schema, presets, single-file rules, and stage handoff now tells one story: the `archiveFormats` list semantics, the per-rid default table, the `--dry-run` / `--verbose` conflict rule, and the `aot` preset name.