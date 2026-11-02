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
| `[test].enable = true` / `run_tests = true` | `test.projects` naming the test projects (e.g. names ending `.Tests` / `.Test`). Declaring this section arms the test gate. | Mapped |
| `[test].enable = false` | Omit the `test` section: the gate passes by default with a warning. | Mapped |
| `[test].run_tests_for_debug`, `[[test.runs]]` (custom configurations/settings/envs) | One Release-config test pass exists; multi-config or filtered runs belong in the repo's own `dotnet test` tooling, not the Manifest. | No ReleaseGen equivalent |
| `[coverage]` (all keys, badge-to-gist) | Nothing: ReleaseGen has no coverage stage. Keep coverage in the repo's `dotnet test` setup (e.g. coverlet.collector) if wanted. | No ReleaseGen equivalent |
| `[github]` `user`, `repo`, `base`, `version_prefix`, `branches` | Nothing: ReleaseGen does not create releases, changelogs, or tags, and git tags do not drive versions — the `<Version>` property in each packable `.csproj` is authoritative. | No ReleaseGen equivalent |
| `[[pack]]` `rid` values (union across packs, ignoring `publish = false` packs) | `build.runtimeIdentifiers` (keep every declared RID; a `rhel-x64` RID commonly maps to `linux-x64` — preserve as declared and let Manifest validation arbitrate). | Mapped |
| `[[pack]]` `kinds = ["zip"]` | The target's `archiveFormats` includes `zip`. | Mapped |
| `[[pack]]` `kinds = ["tar"]` | The target's `archiveFormats` includes `tar.gz` (dotnet-releaser's `tar` means tar.gz; `tar.xz` is not dotnet-releaser-offered — do not introduce it). | Mapped |
| `[[pack]]` `kinds = ["deb"]`, `["rpm"]` | Nothing: no deb/rpm packaging. Suggested next action: distribute via NuGet (`tool-package`) and/or zip/tar.gz archives, or keep deb/rpm production out-of-band. | No ReleaseGen equivalent |
| `[[pack]]` `renamer` | Nothing: ReleaseGen's archive naming is not configurable. | No ReleaseGen equivalent |
| `[[pack]]` `publish = false` | Contributes nothing to the Manifest (no RIDs, no formats). | Mapped |
| Global `profile = "default"` | Expand dotnet-releaser's documented default packs first, then apply the rows above: `win-x64`+`win-arm64` → zip; `linux-x64`+`linux-arm64` → tar (+ deb = no equivalent); `rhel-x64` → tar (+ rpm = no equivalent); `osx-x64`+`osx-arm64` → tar. | Mapped |
| Global `profile = "custom"` | No default expansion; map only the explicitly declared `[[pack]]` entries. | Mapped |
| Global `artifacts_folder` | Fixed: stages read/write only under `<cwd>/release/`. | Mapped (fixed) |
| `[nuget].publish` (default true) and `source` | `publish.sources` (the source URL; omit/empty → nuget.org default; NuGet publish stays on for package-kind targets). | Mapped |
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
- dotnet-releaser's tag-push/branch-conditional publish behavior is CI-level
  workflow logic (`on: push: tags:` filters), not a tool invocation: keep the
  workflow's event filters as-is and note in the report that ReleaseGen accepts
  the resulting command whenever CI decides to run it.
