# Changelog

Notable changes to ReleaseGen, newest first. Package versions come from the static
`<Version>` property in each packable `.csproj`, bumped by hand before each release (see
AGENTS.md) - git tags do not drive them.

## 0.2.0 - 2026-10-26

One adoption-friction batch: two breaking renames, a new archive format, rehearsal and
diagnostics flags, and a helpful `init`.

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
