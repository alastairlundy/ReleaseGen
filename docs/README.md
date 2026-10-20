# ReleaseGen documentation

The full documentation set. Start with the [README](../README.md) for install and the
quickstart.

## User guide

- [Command reference](commands.md) - the six commands: `init`, `build`, `test`, `package`,
  `publish`, and the chained run `run`
- [Manifest schema](manifest-schema.md) - every `releasegen.json` key, per stage section
- [Build presets](presets.md) - the four shipped presets and none
- [Single-file rules](single-file-rules.md) - when single-file applies, the incompatible-API
  reference, and the opt-out
- [Trusted publishing in CI](trusted-publishing.md) - the two-job procedure
- [Stage handoff contract](stage-handoff.md) - the cross-process artifact-location story

## Project records

- [Glossary](../GLOSSARY.md) - the domain vocabulary (Manifest, Stage, Chained run, Test
  gate, Target, Release artifact, Build preset)
- [Decision ledger](decisions/DECISIONS-ReleaseGen-v1-release.md) - the resolved decisions
  behind v1
