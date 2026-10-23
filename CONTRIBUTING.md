# Contributing to ReleaseGen

Thanks for your interest in contributing. The
guidelines below keep changes reviewable.

## Prerequisites

- .NET SDK 10.0.x (the CI pins `10.0.x`; the repo's `global.json` selects the
  Microsoft.Testing.Platform runner).
- git.

A newer SDK (e.g. the 11.0 preview) will build the project fine; the warning about using a preview SDK is harmless.

## Clone and build

```bash
git clone https://github.com/alastairlundy/ReleaseGen.git
cd ReleaseGen
dotnet build src/ReleaseGen.slnx
```

## Run the tests

ReleaseGen uses TUnit on Microsoft.Testing.Platform:

```bash
dotnet test --project src/ReleaseGen.Tests/ReleaseGen.Tests.csproj
```

All tests must pass before a pull request is opened. Add or update tests for any
behavior change.

## Project layout

- `src/ReleaseGenCli/` — the tool entry point (`releasegen` command, `Program.cs`).
- `src/ReleaseGenLib/` — core library (build/test/package/publish stages, manifest
  model, presets, archiving).
- `src/ReleaseGen.Tests/` — TUnit test suite.
- `docs/` — command reference, manifest schema, presets, single-file rules, trusted
  publishing, stage handoff contract.
- `GLOSSARY.md` — domain vocabulary (Manifest, Stage, Chained run, Test gate, etc.).

## Making changes

- Branch from `main`.
- **One major change or feature per branch.** If you are working on multiple major
  features, create a separate branch for each and open separate pull requests.
- Keep commits focused and small.
- Follow the existing conventions:
  - Nullable reference types are enabled; implicit usings are per-project.
  - Central Package Management via `src/Directory.Packages.props` — add new package
    versions there, not inline in `.csproj` files.
  - Target framework is `net10.0` only.

## AI usage disclosure

If you use AI tooling (Copilot, ChatGPT, Claude, etc.) to produce any part of a
commit or pull request, **disclose it**:

- In commit messages, note AI assistance (e.g. `Co-authored-by` or a short
  `AI-assisted:` note). Note that GitHub attributes `Co-authored-by` trailers to
  GitHub user names, not display names — use the AI/bot account's GitHub username
  (e.g. `Co-authored-by: your-bot <your-bot@users.noreply.github.com>`).
- In the body of the pull request, disclose how much of the change was
  AI-assisted and the nature of that assistance (e.g. which parts were
  generated, refactored, or reviewed by AI), along with which LLM(s) was used.

Reviewers need this to calibrate review depth. Undisclosed AI-generated code may be rejected.

## Pull requests

- Open PRs against `main`.
- CI (build + test on ubuntu) must be green.
- Describe the change, link the related issue, and note any follow-ups.
- Do not weaken manifest validation or the publish test gate.

## License

Contributions are licensed under the Mozilla Public License 2.0, the same license
as the project (`LICENSE.txt`). By submitting a pull request you agree to license
your contribution under MPL-2.0.

## Issues

Use GitHub Issues for bugs and feature requests. Triage labels follow the roles
documented in `docs/agents/triage-labels.md`.