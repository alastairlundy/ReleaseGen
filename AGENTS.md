# ReleaseGen

ReleaseGen is an opinionated .NET CLI tool (`releasegen`) for building, packaging, and publishing .NET projects, driven by one strict `releasegen.json` manifest that names the build, test, package, and publish stages.

## Commands

- Build: `dotnet build src/ReleaseGen.slnx`. The solution file is `.slnx` (XML format), not `.sln`.
- Tests: `dotnet test --project src/ReleaseGen.Tests/ReleaseGen.Tests.csproj`. Runs TUnit on Microsoft.Testing.Platform, selected by the root `global.json`. It is not VSTest, so VSTest-era options like `--logger trx` don't apply (reports land in `TestResults/` as `*.tunit-report.json`/`.html`).
- CI (`.github/workflows/ci.yml`) runs restore, then `dotnet build src/ReleaseGen.slnx --configuration Release --no-restore`, then `dotnet test ... --no-build`. Match this order locally before opening a PR.

## Conventions and gotchas

- Central Package Management: add or change package versions only in `src/Directory.Packages.props`; `PackageReference`s in `.csproj` files omit versions.
- Target frameworks: all three projects multi-target `net10.0;net11.0` (with `Microsoft.Build` versioned per TFM in `src/Directory.Packages.props` — 18.9.6 on net10.0, current release on net11.0); `LangVersion` 14.
- `ReleaseGenLib` has `ImplicitUsings` **disabled**. Write explicit `using` statements after the namespace declaration. The CLI and test projects have implicit usings enabled.
- Adding or renaming a manifest field requires updating `ManifestJsonContext` (source-generated `System.Text.Json` context with strict `UnmappedMemberHandling.Disallow`) alongside the model in `src/ReleaseGenLib/Models/`.
- Versioning is static. The `<Version>` property in each packable `.csproj` is authoritative (MinVer was removed) and must be bumped manually before each release. Git tags do not drive package versions.

## Hard constraints

- Do not weaken manifest validation (`ManifestLoader`) or the publish test gate (publish blocks when the manifest declares a test section and that stage fails). CONTRIBUTING.md states this explicitly.

## Testing quirks

- Command-level tests mutate `Environment.CurrentDirectory`. Any new test that exercises stage commands must go through `TestWorkspace.InDirectoryAsync`, which serializes such tests behind one lock and restores the cwd; pure unit tests (parsing, presets, archives) stay parallel.
- Stages discover `releasegen.json` by walking up from the current directory and read/write artifacts only under `<cwd>/release/`; separately invoked stages hand off solely via the `release/` tree (see `docs/stage-handoff.md`).

## Docs map

- `docs/commands.md`, `docs/manifest-schema.md`, `docs/presets.md`, `docs/trusted-publishing.md`, `docs/stage-handoff.md`.
- `docs/single-file-rules.md`: single-file defaults are on for Unix-based OSes, but on Windows only when the target's archive format is zip; the manifest's `build.singleFile` key is the opt-out.
- Use the domain vocabulary from `GLOSSARY.md` (Manifest, Stage, Chained run, Test gate, Build preset, Target) in code, tests, and issue text.

## Workflow expectations (from CONTRIBUTING.md)

- Branch from `main`; one major change or feature per branch; all tests must pass before a PR is opened.
- Disclose AI assistance in commit messages (e.g. `AI-assisted:` or `Co-authored-by` using the bot's GitHub username) and in the PR body (what was AI-generated and which LLMs were used). Undisclosed AI-generated code may be rejected.

## Agent skills

### Issue tracker

Issues and PRDs live as GitHub issues, managed via the gh CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical roles: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout; domain vocabulary lives in GLOSSARY.md at the repo root. See `docs/agents/domain.md`.
