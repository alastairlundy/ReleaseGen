# Mapping table — dotnet-releaser → ReleaseGen

Load this file when translation (SKILL.md Step 4) is about to begin. It is the
skills' fixed mapping from dotnet-releaser's `dotnet-releaser.toml` (TOML) and
CLI to ReleaseGen's `releasegen.json` Manifest and stage commands. The latest
installed `releasegen` CLI is authoritative over every row here when they
conflict (SKILL.md Gotchas); record any conflict in the migration report.

## Source config sections → Manifest / report buckets

| dotnet-releaser.toml | ReleaseGen destination | Bucket |
|---|---|---|
| `[msbuild].project` (list of csproj/fsproj/vbproj, sln, slnx) | `targets[].project` — one target per release project; expand listed solutions to their projects. Test projects go to `test.projects` instead. Never target a project with `IsPackable=false`. | Mapped |
| `[msbuild].configuration` | Not configurable: ReleaseGen's build stage always publishes `-c Release`. | Mapped (fixed) |
| `[msbuild].properties` `PublishAot`, `PublishTrimmed`, `PublishReadyToRun`, `SelfContained` | The selected `build.preset` (`aot`, `ready-to-run`, `trimming-only`, `framework-dependent`) or preset `none` plus explicit `publishAot` / `publishTrimmed` / `readyToRun` / `selfContained` keys. Rule: if the resolved set equals one shipped preset, write that preset name; otherwise write preset `none` plus the explicit per-key bools. Note: no shipped preset combines ready-to-run with trimming — dotnet-releaser's default property set (trimmed + single-file + self-contained + ready-to-run) resolves to preset `none` plus explicit keys. | Mapped |
| `[msbuild].properties` (any other MSBuild property, e.g. `PublishSingleFile` nuances) | Not manifest-representable: set it where the project's MSBuild properties are declared — the project file or an imported props file such as `Directory.Build.props`. ReleaseGen reads expanded properties and never passes switches itself. | Manual action required |
| `[msbuild].build_debug`, `[msbuild].configuration_debug` | No key: ReleaseGen builds Release only. | No ReleaseGen equivalent |
| `[test]` section absent (dotnet-releaser default) | `test.projects` naming the discovered test projects (e.g. names ending `.Tests` / `.Test`): dotnet-releaser defaults `enable` and `run_tests` to `true`, so it tests even with no `[test]` section — declare the section to preserve that, which arms the test gate; when no test projects are discovered, omit the section (dotnet-releaser would run none either). | Mapped |
| `[test].enable = true` / `run_tests = true` | `test.projects` naming the test projects (e.g. names ending `.Tests` / `.Test`). Declaring this section arms the test gate. | Mapped |
| `[test].run_tests = false` | Omit the `test` section: the Release-config test pass is off, so the test gate is not armed and passes with a warning; any remaining debug or custom runs map per the `run_tests_for_debug` / `[[test.runs]]` row. | Mapped |
| `[test].enable = false` | Omit the `test` section: the gate passes by default with a warning. | Mapped |
| `[test].run_tests_for_debug`, `[[test.runs]]` (custom configurations/settings/envs) | One Release-config test pass exists; multi-config or filtered runs belong in the repo's own `dotnet test` tooling, not the Manifest. | No ReleaseGen equivalent |
| `[coverage]` (all keys, badge-to-gist) | Nothing: ReleaseGen has no coverage stage. Keep coverage in the repo's `dotnet test` setup (e.g. coverlet.collector) if wanted. | No ReleaseGen equivalent |
| `[github]` `user`, `repo`, `base`, `version_prefix`, `branches` | Nothing: ReleaseGen does not create releases, changelogs, or tags, and git tags do not drive versions — the `<Version>` property in each packable `.csproj` is authoritative. | No ReleaseGen equivalent |
| `[[pack]]` `rid` values (union across packs, ignoring `publish = false` packs) | `build.runtimeIdentifiers` (keep every declared RID; a `rhel-x64` RID commonly maps to `linux-x64` — preserve as declared and let Manifest validation arbitrate). The list is global: the build stage publishes every target once per RID in it, so a RID cannot be scoped to one target. | Mapped |
| `[[pack]]` `kinds = ["zip"]` / `["tar"]` identical across every pack | The target's `archiveFormats` includes `zip` / `tar.gz` (dotnet-releaser's `tar` means tar.gz; `tar.xz` is not dotnet-releaser-offered — do not introduce it). Declare `archiveFormats` only when one format set covers every RID of the build. | Mapped |
| `[[pack]]` `kinds` that differ per RID (e.g. `win-x64` → zip, `linux-x64` → tar) | Do not union them into one target-wide `archiveFormats`: there is no per-RID representation — a declared key applies to every RID of the build, and splitting targets does not isolate RIDs (`build.runtimeIdentifiers` is global, so targets × RIDs is a fixed cross product). Prefer omitting `archiveFormats`: the absent-key RID-class defaults (`win-*`/`any-*` → zip; Unix-based → zip + `tar.gz`) keep each RID's usual formats (`tar` ≡ `tar.gz`) and add only a zip on Unix-based RIDs. A format outside its RID's default set (e.g. `tar` for `win-*`) cannot be preserved either way — record any archive added or lost by this in the migration report. | Manual action required |
| `[[pack]]` `kinds = ["deb"]`, `["rpm"]` | Nothing: no deb/rpm packaging. Suggested next action: distribute via NuGet (`tool-package`) and/or zip/tar.gz archives, or keep deb/rpm production out-of-band. | No ReleaseGen equivalent |
| `[[pack]]` `renamer` | Nothing: ReleaseGen's archive naming is not configurable. | No ReleaseGen equivalent |
| `[[pack]]` `publish = false` | Contributes nothing to the Manifest (no RIDs, no formats). | Mapped |
| Global `profile = "default"` | Expand dotnet-releaser's documented default packs first, then apply the rows above: `win-x64`+`win-arm64` → zip; `linux-x64`+`linux-arm64` → tar (+ deb = no equivalent); `rhel-x64` → tar (+ rpm = no equivalent); `osx-x64`+`osx-arm64` → tar. The formats differ per RID class, so omit `archiveFormats` and let the RID-class defaults apply (per the per-RID row above) instead of declaring the union. | Manual action required |
| Global `profile = "custom"` | No default expansion; map only the explicitly declared `[[pack]]` entries. | Mapped |
| Global `artifacts_folder` | Fixed: stages read/write only under `<cwd>/release/`. | Mapped (fixed) |
| `[nuget].source` | `publish.sources` (the source URL; omit/empty → nuget.org default). | Mapped |
| `[nuget].publish = false` (key defaults to true) | No manifest key: `publish.sources` maps the source URL only and never turns the push off — ReleaseGen always adds a publish stage for package-kind targets, so `releasegen run` publishes unconditionally (strict parse would reject an invented enable key). Preserve `publish = false` by keeping packaging and publishing as separate stage invocations (`releasegen build` / `releasegen package`, omitting `releasegen publish`) instead of the chained run, or by an explicit CI guard before any `releasegen run`. | Manual action required |
| `[nuget].trusted_publishing`, `user`, token-service/audience keys | No manifest key: API keys travel via `NUGET_API_KEY` or `--api-key`, never the Manifest; in CI prefer a short-lived key from the OIDC token exchange (see SKILL.md Gotchas). | Mapped (key handling) |
| `[brew]` (Homebrew tap/formula) | Nothing. Suggested next action: publish a Homebrew tap out-of-band or drop it. | No ReleaseGen equivalent |
| `[scoop]` (Scoop bucket/manifest) | Nothing. Suggested next action: publish a Scoop bucket out-of-band or drop it. | No ReleaseGen equivalent |
| `[changelog]` | Nothing: no changelog generation or GitHub release notes. | No ReleaseGen equivalent |
| `[service]`, `[service.systemd]` (service packaging) | Nothing: no service packaging (also bounded by deb/rpm having no equivalent). | No ReleaseGen equivalent |
| `[[deb.depends]]`, `[[rpm.depends]]` | Nothing (dependent on the deb/rpm rows above). | No ReleaseGen equivalent |

## CI command mapping → releasegen invocations

| dotnet-releaser CLI use | releasegen equivalent |
|---|---|
| `dotnet tool install --global dotnet-releaser` (or `-g`) | `dotnet tool install -g ReleaseGen` (long form `--global`) — the skill never runs this itself; it points the user at it |
| `dotnet-releaser new [--project <p>]` | `releasegen init` (non-interactive detection; `--interactive` exists but needs a console on stdin) |
| `dotnet-releaser build <toml> [--force]` | `releasegen build` (run from the directory the Manifest lives at; `--force` has no equivalent — the build stage manages its `release/` output itself) |
| `dotnet-releaser publish <toml> [--force-upload]` | `releasegen run` (build + package + publish in one chained run; idempotent like the original's `--force-upload` note is out of scope) |
| `dotnet-releaser run <toml>` (GitHub-Action auto mode) | `releasegen run` (the chained run; branch/tag-conditional behavior needs CI-level conditions, see below) |
| `dotnet-releaser changelog …` | No Rewrite: no equivalent; flag the invocation line, add the manual-action comment, do not rewrite |
| `--github-token`, `--nuget-token`, `--github-token-extra` | No GitHub-equivalent flags exist; the NuGet key goes to `NUGET_API_KEY`/`--api-key` per the gotcha; GitHub upload needs no token because ReleaseGen does not upload to GitHub |
| `permissions: id-token: write` (NuGet trusted publishing) | Keep the permission key when the OIDC short-lived key exchange is still used for the NuGet push |
| `permissions: contents: write` (release/changelog/asset upload) | No longer required by the migrated release command; leave existing permissions edits to the user unless they ask |

## Flagging rules (applied during CI rewrite, SKILL.md Step 7)

- Rewrite an invocation line only when a `releasegen` equivalent exists in the CI
  command mapping above.
- Any invocation whose behavior depends on a `No ReleaseGen equivalent` row keeps
  its line unchanged, gains the
  `# MIGRATION: manual action pending (see releasegen-migration-report.md)`
  comment, and is logged under the report's `Manual action required` heading.
- `dotnet-releaser run` (GitHub-Action auto mode) decides publish vs
  build-only from the GitHub event inside the tool; `releasegen run` publishes
  whenever CI invokes it. Before rewriting such a line, require CI conditions
  (workflow event filters or step/job conditions) that keep package and publish
  off ordinary branch pushes and on version-tag pushes, or split the
  invocations so branch pushes run `releasegen build` and version-tag pushes
  run `releasegen run` (or, in stage order, `releasegen build` then
  `releasegen package` plus `releasegen publish` — the build stage is what
  produces the release archives that package and publish do not).
  When neither can be applied without restructuring the workflow beyond its
  invocation lines, leave the invocation unchanged, add the
  `# MIGRATION: manual action pending (see releasegen-migration-report.md)`
  comment, and record the required tag-only publish condition under the
  report's `Manual action required` heading.
