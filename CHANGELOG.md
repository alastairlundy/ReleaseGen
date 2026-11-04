# Changelog

Notable changes to ReleaseGen, newest first. Package versions come from the static
`<Version>` property in each packable `.csproj`, bumped by hand before each release (see
AGENTS.md) - git tags do not drive them.

## 0.3.0 - 2026-11-04

Duplicate-push control for the publish stage, plus routine dependency updates.

### Added

- **`--skip-duplicate` on `publish`**: when skipping is enabled, the flag is passed to
  every `dotnet nuget push`. ReleaseGen performs no pre-check - the package source alone
  decides what counts as a duplicate. A push that exits 0 with the skip marker in its
  output is counted as skipped instead of pushed: the marker line is reported as a stage
  note naming the package and the source, and the closing summary gives pushed and
  skipped as two totals while the stage still exits 0. A push that exits nonzero still
  fails the stage as before. A `--dry-run` plan shows the resolved skip mode once per
  source (only for manifests with package kinds to publish) along with the flag in the
  planned command line.
- **`skipDuplicates` manifest key**: the publish section gains a nullable bool, so a
  manifest can state its standing answer for duplicate pushes. The key is matched with
  exact casing under the strict parse, so a wrong-cased key fails naming the property,
  and an absent key keeps today's fail-fast behavior. When the manifest and the command
  line both state a value and the two differ, `--skip-duplicate` wins and one conflict
  line goes to standard error, at most once per run; a dry-run plan prints the same line
  a real run would.
- **`--skip-duplicate` forwarded from the chained run**: `releasegen run` accepts the
  same flag and passes it to publish, so a chained run and a standalone
  `releasegen publish` resolve one skip decision. The value survives the hop in all
  three states, so a plain `releasegen run` can never conflict with the manifest: the
  flag on its own or with `true` turns skipping on, and `--skip-duplicate false`
  forwards an explicit "off" rather than collapsing into absence.

### Changed

- Dependencies: System.CommandLine to 2.0.14, Microsoft.Extensions.DependencyInjection
  (and Abstractions) to 10.0.14, Meziantou.Analyzer to 3.0.318.
- Documentation for commands, manifest schema, and the glossary now covers duplicate
  pushes: the `skipDuplicates` key, the `--skip-duplicate` flag on `publish` and `run`,
  how the two channels are reconciled into one decision per run, and the pushed/skipped
  two-total summary.

## 0.2.0 - 2026-11-01

### Breaking changes

- **Build preset rename**: `aot-plus-trimming` is now `aot`. The old name is neither
  aliased nor normalized, so a manifest (or `--preset`) carrying it fails with the usual
  unknown-preset error, whose known set reads `aot`, `ready-to-run`, `trimming-only`,
  `framework-dependent`, `none`. Rename the preset before upgrading.
- **Manifest key rename**: each target's `archive` key is now `archiveFormats`. It accepts
  a bare string or a list (`"zip"` or `["zip", "tar.gz"]`), both normalizing to one list at
  parse time, and each format may appear at most once - a duplicate fails validation naming
  the target and the format. Manifests are parsed strictly, so a leftover `archive` key fails
  as an unknown field - rename it to `archiveFormats`.
- **Unix archive defaults**: an absent `archiveFormats` key used to mean zip everywhere. It
  now resolves to zip plus tar.gz for Unix-based rids (`linux-*`, `osx-*`), while
  Windows-class (`win-*`) and portable (`any-*`) rids keep zip alone. `tar.xz` is never a
  default on any platform. Declare the key explicitly to pin the previous behavior.

### Added

- **`--dry-run`** on every stage command and on the chained run: full planning with zero
  writes - nothing under `release/` or `TestResults/`, the publish stage prints the test
  projects its gate would have run instead of running them, and the exit code reflects
  manifest validation alone. A zero-exit dry run is a plan, not a release.
- **`--verbose`** on the same commands: each spawned command line with its raw output and a
  `cause:` line explaining it, reading the same plan the dry-run report prints. Every
  default (non-verbose) failure gains a `hint: run with --verbose for full detail` line, and
  passing `--quiet` together with `--verbose` fails with a one-line conflict message rather
  than silently picking an output mode.
- **`tar.xz` archives**: each target produces one archive per declared format per
  (target, rid). `tar.xz` is opt-in, is written through the system `xz` binary, and is
  skipped for Windows-class and portable rids with a warning - or fails validation when no
  format remains for those rids. A missing `xz` on PATH fails the stage naming the tool to
  install.
- **`releasegen init --interactive`**: a five-question questionnaire (build preset, projects,
  runtime identifiers, archive formats, test projects) that writes a tailored manifest, with
  defaults read from repo detection. It needs a console on stdin; piped input fails with an
  error pointing at plain `releasegen init`. A lone `tar.xz` answer is refused at the prompt
  when the chosen rids include rids tar.xz cannot be produced for.
- **Detection-seeded `init`**: the default non-interactive path scans the repository for .NET
  projects, seeds executable targets, test projects, and package kinds it can classify with
  certainty, warns once for each classification it cannot make, and falls back to the minimal
  skeleton when detection fails. Both init write paths validate the composed manifest through
  the strict loader before writing, so a validation failure writes nothing; detection skips
  junctions and symlinked directories, so a link cycle cannot diverge the scan.
- **`tool-package` validation**: a target declaring the `tool-package` kind is checked
  against the project's evaluated `PackAsTool` property and fails the build naming the csproj
  fix when it is not `true`.

### Changed

- `init` writes a richer skeleton: executable targets carry an explicit `archiveFormats` list
  (`zip`, `tar.gz`) so the default is visible in the file, and a successful write ends with a
  note covering next steps, single-file rules, and archive defaults.
- Documentation for commands, manifest schema, presets, single-file rules, and stage handoff
  now tells one story: the `archiveFormats` list semantics, the per-rid default table, the
  `--dry-run` / `--verbose` conflict rule, and the `aot` preset name.
