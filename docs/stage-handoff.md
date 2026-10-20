# Stage handoff contract

This contract is the load-bearing artifact-location story behind D003 (stage commands remain
invocable separately) and D018 (the two-job trusted-publishing CI procedure). It is referenced
by the chained run (`releasegen run`), which writes the identical tree in one process.

## The invocation-directory rule

Every stage:

- discovers `releasegen.json` by walking up from the **current directory** to the repository
  root (D009), and
- reads and writes its artifacts under `<current directory>/release/` (D012: local output
  only; nothing uploads from the build, test, or package stages).

Separately invoked stages therefore hand artifacts to each other by running from the same
directory. A pipeline that splits stages across processes or jobs must either keep the working
directory or move the `release/` tree intact between them, preserving the layout below.

## Per-stage inputs and outputs

| Stage | Inputs | Outputs (under the invocation directory) |
|-------|--------|------------------------------------------|
| `init` | None; `--preset` seeds the manifest (T013). Not part of the chained run (D003). | `releasegen.json` in the current directory. |
| `build` | Manifest targets and build section; `--preset` overrides `build.preset` per invocation (T013). | `release/<rid>/<project-name>/` holds one target's publish binaries for that rid; `release/<project-name>-<rid>.zip` (or `.tar.gz`) sits beside the per-rid folders for executables-kind targets (zip is the default, T016). Package-kind-only targets produce no archives. |
| `test` | The manifest's test section projects, or the current directory when none are listed (T011). | None on disk. The test gate outcome (D004) is in-process state: a separately invoked `publish` consults the gate by running the test stage itself. |
| `package` | Each target's declared package kinds (D013). | `release/packages/lib/<project-name>/*.nupkg` for `lib-package`, `release/packages/tool/<project-name>/*.nupkg` for `tool-package`, plus any `.snupkg` pack emits beside them. The `executables` kind is the build stage's product and produces no package. |
| `publish` | The `.nupkg` files under `release/packages/…` (the package stage's output), the manifest's `publish.sources` or the nuget.org default (D011), and the API key from `--api-key` or the `NUGET_API_KEY` environment variable (D011). | None on disk. Push results are textual (T012). The push list is collected from the artifact tree before the first push, so `publish` needs the tree present, not the stages re-run. |

## Stage order and the test gate

The stages form one pipeline in manifest order: `build` → `test` → `package` → `publish`.
`package` depends only on the manifest (it packs from source), not on `build` output; `publish`
depends only on the package tree. The test gate (D004) blocks `publish` when the manifest
declares a test section and the test stage exits nonzero; a manifest with no test section
publishes with a warning. `run` aborts at the first failed stage, so a failed test stage stops
the chain before `publish` (D010).

## Two-job CI procedure (D018)

NuGet's trusted-publishing posture puts credential acquisition with the CI platform, not the
release tool; ReleaseGen performs no OIDC token acquisition. The safe pattern is two jobs:

1. **Job 1 - build through package**: check out the repository and run `releasegen run`'s
   stages up to packaging (`releasegen build`, `releasegen test`, `releasegen package`), then
   upload the `release/` tree as a pipeline artifact.
2. **Job 2 - publish**: download the `release/` tree into the same directory layout, exchange
   the CI credentials for a fresh short-lived nuget.org key (valid about one hour), deliver it
   through the `NUGET_API_KEY` environment channel, and run `releasegen publish`. When the
   manifest declares a test section, this consults the test gate by running the test stage in
   the publish job, so the gate holds even across the job boundary (D004).

Trusted publishing is preferred in CI; the API key path documented above remains the local and
manual route (D019).

## What is not part of the contract

- In-process state (`StageContext`) is never shared across processes; only the `release/` tree
  and `releasegen.json` cross process boundaries.
- No stage uploads build binaries or archives anywhere (D012); the only network destination in
  v1 is the publish stage's NuGet source (D011).
