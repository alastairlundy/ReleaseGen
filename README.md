# ReleaseGen

[![CI](https://img.shields.io/github/actions/workflow/status/alastairlundy/ReleaseGen/ci.yml?branch=main)](https://github.com/alastairlundy/ReleaseGen/actions/workflows/ci.yml)
[![License: MPL-2.0](https://img.shields.io/github/license/alastairlundy/ReleaseGen)](https://github.com/alastairlundy/ReleaseGen/blob/main/LICENSE.txt)

ReleaseGen is an opinionated .NET CLI for building, packaging, and publishing .NET projects.
One strict `releasegen.json` manifest (the Manifest) drives every stage of a release: build,
test, package, and publish.

## Install

```bash
dotnet tool install -g ReleaseGen
```

The tool command name is `releasegen`. Running bare `releasegen` prints command help; no stage
executes without an explicit verb.

## Quickstart

```bash
# 1. Write a minimal releasegen.json in your repository
releasegen init

# 2. Edit releasegen.json: name your targets (projects and their artifact kinds),
#    declare runtimeIdentifiers, and optionally add test and publish sections

# 3. Run the whole pipeline - build, test, package, publish - in manifest order
releasegen run
```

`init` refuses to overwrite an existing manifest unless you pass `--overwrite`, and
`--preset <name>` seeds the manifest's build preset. `run` is the chained run: it executes the
stages the manifest names in manifest order, stops at the first failed stage, and notes any
skipped stages. Each stage is also its own command - `releasegen build`, `releasegen test`,
`releasegen package`, and `releasegen publish` - and all of them share the same Manifest.

Build output stays local under a `release/` directory; the only network destination is the
publish stage's NuGet source. Publishing is gated on your tests: a Manifest declaring a test
section blocks publish when that section's stage fails.

## Documentation

- [Command reference](https://github.com/alastairlundy/ReleaseGen/blob/main/docs/commands.md)
- [Manifest schema](https://github.com/alastairlundy/ReleaseGen/blob/main/docs/manifest-schema.md)
- [Build presets](https://github.com/alastairlundy/ReleaseGen/blob/main/docs/presets.md)
- [Single-file rules](https://github.com/alastairlundy/ReleaseGen/blob/main/docs/single-file-rules.md)
- [Trusted publishing in CI](https://github.com/alastairlundy/ReleaseGen/blob/main/docs/trusted-publishing.md)
- [Stage handoff contract](https://github.com/alastairlundy/ReleaseGen/blob/main/docs/stage-handoff.md)
- [Glossary](https://github.com/alastairlundy/ReleaseGen/blob/main/GLOSSARY.md)

## What ReleaseGen isn't

- A changelog or release notes generator/formatter.
- A test coverage checker.
- An artifact uploader: ReleaseGen writes release binaries and archives to a local `release/`
  directory and performs no upload of them.
- A token broker: ReleaseGen performs no OIDC token acquisition; see the trusted publishing
  page for the CI-side procedure.
