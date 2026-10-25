# ReleaseGen

[![Latest NuGet](https://img.shields.io/nuget/v/ReleaseGen?style=flat-square&label=Latest%20Stable%20Release)](https://www.nuget.org/packages/ReleaseGen/)
[![Latest Pre-release NuGet](https://img.shields.io/nuget/vpre/ReleaseGen?style=flat-square&label=Latest%20Pre-Release)](https://www.nuget.org/packages/ReleaseGen/)
[![Downloads](https://img.shields.io/nuget/dt/ReleaseGen?style=flat-square)](https://www.nuget.org/packages/ReleaseGen/)
[![CI](https://img.shields.io/github/actions/workflow/status/alastairlundy/ReleaseGen/ci.yml?branch=main&style=flat-square)](https://github.com/alastairlundy/ReleaseGen/actions/workflows/ci.yml)
[![License: MPL-2.0](https://img.shields.io/github/license/alastairlundy/ReleaseGen?style=flat-square)](https://github.com/alastairlundy/ReleaseGen/blob/main/LICENSE.txt)


ReleaseGen is an opinionated .NET CLI for building, packaging, and publishing .NET projects. One strict `releasegen.json` manifest drives every stage of a release: build, test, package, and publish.

## Install

```bash
dotnet tool install -g ReleaseGen
```

Requires the .NET 10 SDK. The tool command is `releasegen`. Running bare `releasegen` prints command help; no stage executes without an explicit verb.

## Quickstart

```bash
# 1. Write a minimal releasegen.json in the current directory
releasegen init

# 2. Edit releasegen.json: name your targets (projects and their artifact kinds),
#    declare runtimeIdentifiers, and optionally add test and publish sections

# 3. Run the pipeline the manifest names, in manifest order
releasegen run
```

Notes:

- `init` is a one-shot command and takes no part in the chained run. It refuses to overwrite an existing manifest unless you pass `--overwrite`; `--preset <name>` seeds the manifest's
  `build.preset` key.
- `run` is the chained run: it executes the stages the manifest names - build always, test when a test section is declared, package and publish when targets declare package kinds - in manifest order, aborting at the first failed stage and noting each skipped stage. The test outcome is carried to publish, so tests never run twice.
- Each stage is also its own command - `releasegen build`, `releasegen test`, `releasegen package`, `releasegen publish` - and all of them share the same manifest, discovered by walking up from the current directory.

Build output stays local under a `release/` directory; the only network destination is the publish stage's NuGet source. Publishing is gated on your tests: a manifest declaring a test section blocks publish when that stage fails.

## Commands

| Command | Purpose |
|---|---|
| `init` | Write a minimal manifest (one-shot) |
| `build` | Publish release binaries per target and runtime identifier |
| `test` | Run the projects the manifest's test section names |
| `package` | Pack the package kinds each target declares |
| `publish` | Push packages to NuGet sources (test-gated) |
| `run` | Chained run of the stages above, in manifest order |

Every command accepts the global `--quiet` flag; exit code is `0` on success and `1` on any failure. See the [command reference](docs/commands.md) for flags and behavior.

## Documentation

- [Command reference](docs/commands.md)
- [Manifest schema](docs/manifest-schema.md)
- [Build presets](docs/presets.md)
- [Single-file rules](docs/single-file-rules.md)
- [Trusted publishing in CI](docs/trusted-publishing.md)
- [Stage handoff contract](docs/stage-handoff.md)
- [Glossary](GLOSSARY.md)

## What ReleaseGen isn't

- A changelog or release notes generator/formatter.
- A test coverage checker.
- An artifact uploader: ReleaseGen writes release binaries and archives to a local `release/`
  directory and performs no upload of them.
- A token broker: ReleaseGen performs no OIDC token acquisition; see the trusted publishing page for the CI-side procedure.
