---
name: dotnet-releaser-to-releasegen-migration
description: >-
  Use when a repository contains a dotnet-releaser.toml or dotnet-releaser CI
  usage and the goal is switching the repository to ReleaseGen, or when the
  user explicitly asks to migrate. Do not use for fresh releasegen.json
  manifest authoring with no dotnet-releaser history.
compatibility: Requires ReleaseGen and the .NET 10 SDK or newer
license: MIT
---

# dotnet-releaser-to-releasegen-migration

## When to Use

- When a repository contains a `dotnet-releaser.toml` (dotnet-releaser's TOML
  configuration file) and the goal is switching that repository to ReleaseGen.
- When `.github/workflows/` files or build scripts invoke `dotnet-releaser` and the
  goal is replacing those invocations with ReleaseGen stages.
- When the user explicitly asks to migrate or switch a repository from dotnet-releaser
  to ReleaseGen, even when no `dotnet-releaser.toml` exists on disk.
- When a mapping result is ambiguous or the source key is absent from
  `references/mapping-table.md`, ask the user.

## When Not to Use

- When authoring a fresh `releasegen.json` for a repository that never used
  dotnet-releaser: run `releasegen init` or author the manifest directly instead.
- When a repository already has a valid `releasegen.json` and no dotnet-releaser
  config or CI usage remains anywhere in the repo.
- When the user only wants information about either tool and asks that no files be changed.
- When the repository's release pipeline does not build, package, or publish .NET
  artifacts: migrating any other CI system is out of scope.

## Output Mode

Whenever the workflow passes the trigger step, the skill writes exactly these files
and modifies nothing else: `releasegen.json` (repo root), `releasegen-migration-report.md`
(repo root), and the minimal invocation-line edits inside detected CI files.

## Workflow

The mapping table and CI command mapping live in a reference file so the agent
reads them at translation time, not from memory. When any statement in this skill
(including `references/mapping-table.md`) conflicts with the latest installed
`releasegen` CLI's actual behavior, the CLI is authoritative: follow the CLI and
record the conflict in the migration report. Follow the steps in order; each step
names its completion signal.

- [ ] Step 1: Detect (Branch A)
- [ ] Step 2: Load the mapping reference
- [ ] Step 3: Build the feature inventory
- [ ] Step 4: Translate and write the Manifest
- [ ] Step 5: Classify coverage and write the migration report
- [ ] Step 6: Validate via the dry-run gate
- [ ] Step 7: Rewrite CI invocations
- [ ] Step 8: Run the final audit

1. **Detect.** Search the repository root and config locations for
   `dotnet-releaser.toml`, and grep `.github/workflows/` plus build scripts (`*.sh`,
   `*.ps1`, `*.yml`) for the string `dotnet-releaser`. Found config: record its path
   as the migration input. No config but an explicit migration request: proceed and
   record "source config absent" for the report. Neither: do not run the skill; state
   the reason in one line. Completion: a migration input path, or a one-line refusal.

2. **Load the mapping reference.** When translation (Step 4) is about to begin,
   load `references/mapping-table.md` once and keep its contents for Steps 3 and 4.
   Completion: the mapping table contents are in context; cite one row from it to
   yourself in the running notes.

3. **Inventory.** Read the source TOML config end to end and write an inventory
   listing every source feature: each `[msbuild]` key, every `[[pack]]` entry (RID,
   kinds, publish flag, renamer), each `[test]`, `[coverage]`, `[github]`, `[nuget]`,
   `[brew]`, `[scoop]`, `[changelog]`, `[service]`, and `[[deb.depends]]`/`[[rpm.depends]]`
   section, plus every other top-level key, marking keys absent from the mapping
   table as `unknown`. On the config-absent path, check for an existing
   `releasegen.json` at the repository root before running `releasegen init`: init
   refuses an existing manifest and exits 1 unless `--overwrite` is passed. When
   one exists, load it and merge its contents into the inventory (its keys are
   already Manifest keys, so carry them through instead of marking them `unknown`)
   and ask the user before Step 4 replaces it; when none exists, run
   `releasegen init` and use its seeded skeleton plus the user's description as the
   inventory. Completion: an inventory covering every source section at least once.

4. **Translate.** Map each inventory item through the mapping table and write
   `releasegen.json` at the repository root. Follow the Manifest's rules inline:
   RIDs go to `build.runtimeIdentifiers`; build flags resolve to one of the four
   presets (`aot`, `ready-to-run`, `trimming-only`, `framework-dependent`) or preset
   `none` plus explicit per-key overrides; `zip`/`tar.gz` map to `archiveFormats`;
   NuGet builds become targets declaring `lib-package` or `tool-package`; publish
   targets become `publish.sources`; test projects become `test.projects`. Never
   write a secret into the Manifest. Never invent keys that fail the strict parse.
   When a source key is absent from the mapping table, prefer asking the user over
   guessing. Completion: `releasegen.json` exists at the repository root.

5. **Classify.** Write `releasegen-migration-report.md` at the repository root with
   three headings: `Mapped`, `Manual action required`, and `No ReleaseGen
   equivalent`. Place every Step 3 inventory item under exactly one heading, each
   entry carrying its source key, its target key or suggested next action, and —
   for CI invocations found in Step 1 — a `Pending CI rewrite` note resolved in
   Step 7. Completion: the report file exists with all three headings and one entry
   per inventory item.

6. **Validate.** This step requires the `releasegen` CLI, which the skill never
   installs: if it is missing, point the user at
   `dotnet tool install -g ReleaseGen` (long form `--global`) and continue only
   after they run it. From the repository root, run `releasegen build --dry-run`
   (or `releasegen run --dry-run` when staged invocations were detected). Exit 0:
   the manifest passes strict parse and validation; proceed to Step 7. Nonzero:
   fix `releasegen.json` per the error message naming the offending field, then
   re-run. After three consecutive failed loops, stop and present the remaining
   errors to the user. Completion: exit code 0, or three recorded failure loops.

7. **Rewrite CI.** Run only after Step 6 exits 0. For each CI invocation from
   Step 1, apply the fixed command mapping in the CI section of
   `references/mapping-table.md`: a full chained release becomes `releasegen run`;
   a `build`-only invocation becomes `releasegen build`; `dotnet-releaser new`
   becomes `releasegen init`; a tool installation step points at installing
   ReleaseGen the same way as Step 6. Change only invocation lines; preserve all
   surrounding YAML and CI structure. An invocation depending on a no-equivalent
   feature stays unchanged, gains a
   `# MIGRATION: manual action pending (see releasegen-migration-report.md)`
   comment, and becomes an entry under the report's `Manual action required`
   heading. Append every rewrite made here to the report. Completion: grepping the
   rewritten CI files returns zero `dotnet-releaser` matches outside flagged
   comment lines.

8. **Audit.** Verify and state, in one summary message to the user: the files
   changed (`releasegen.json`, `releasegen-migration-report.md`, each CI file),
   the count of entries in each report bucket, and the Step 6 gate result.
   Completion: the summary message names all three.

## Gotchas

- `tar.xz` is opt-in only and never produced for `win-*` or `any-*` RIDs; a target
  declaring it as its only format fails validation for such RIDs.
- Absent `archiveFormats` resolves per RID class (Windows-class: `zip`; Unix-based:
  `zip`, `tar.gz`); an explicit key overrides for every RID of the target.
- A target declaring `executables` whose project evaluates to a library output fails
  Manifest validation; `tool-package` requires the expanded `PackAsTool=true`, and
  ReleaseGen reads expanded MSBuild properties, so the project files stay authoritative.
- Unknown Manifest fields fail the strict parse by name; the Manifest has no
  secret-bearing field. Pass the API key through the `NUGET_API_KEY` environment
  variable by default; pass it via `--api-key` only in CI, where a short-lived
  API key generated from an OIDC token exchange is acceptable.
- Declaring a `test` section arms the test gate: publishing blocks when that stage
  fails; omitting it passes by default with a warning.
- A zero-exit dry run proves manifest validity only; it is a plan, not certification.
- The latest installed `releasegen` CLI is authoritative over any statement in this
  skill; when they disagree, follow the CLI and record the conflict in the migration
  report.

## Validation

- [ ] `releasegen build --dry-run` exits 0 from the repository root (Yes/No)
- [ ] `releasegen.json` parses with no invented keys and contains no secret value (Yes/No)
- [ ] `releasegen-migration-report.md` exists with all three bucket headings and
      at least one entry per Step 3 inventory item, each carrying a source key and
      a target key or suggested action (Yes/No)
- [ ] Every entry classified under `No ReleaseGen equivalent` names a suggested
      next action (Yes/No)
- [ ] Rewritten CI files contain zero `dotnet-releaser` matches outside flagged
      comment lines (Yes/No)
- [ ] The Step 8 audit summary names all changed files and the bucket counts (Yes/No)
- [ ] Any conflict between this skill's statements and the installed `releasegen`
      CLI's behavior was resolved by following the CLI and recorded in the
      migration report (Yes/No)
