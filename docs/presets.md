# Build presets

A build preset is a named set of build settings (AOT, trimming, ready-to-run, self-contained,
and the single-file rules) applied to a rid's publish when the Manifest or the user selects it.
Presets make ReleaseGen's opinionated defaults named and selectable instead of buried
in code.

## The shipped set

Four named presets ship in v1, plus none. No preset combines ready-to-run with trimming; this
is the shipped set.

| Preset name | What it applies |
|--------------|-----------------|
| `aot` | Native AOT plus assembly trimming, self-contained. Trimming is implied by AOT, so no separate trimming switch is emitted. |
| `ready-to-run` | Ready-to-run compilation, self-contained, without trimming. |
| `trimming-only` | Assembly trimming, self-contained, without ready-to-run. |
| `framework-dependent` | Framework-dependent deployment with runtime roll-forward enabled for minor versions and patch versions (`RollForward=Minor`). |
| `none` | No preset applied: only what the Manifest's build section specifies runs - no ReleaseGen-implicit switches. |

Unknown preset names fail with an error listing the known set.

## How presets map to publish

The build stage runs `dotnet publish -c Release -r <rid>` once per declared runtime
identifier, and the selected preset resolves to publish switches per rid:

| Preset | Emitted switches |
|--------|------------------|
| `aot` | `PublishAot=true` (implies trimming), `--self-contained=true` |
| `ready-to-run` | `PublishReadyToRun=true`, `--self-contained=true` |
| `trimming-only` | `PublishTrimmed=true`, `--self-contained=true` |
| `framework-dependent` | `--self-contained=false`, `RollForward=Minor` |
| `none` | Nothing implicit |

Only enabling switches are emitted, so a project file's own settings stay authoritative
wherever ReleaseGen has no opinion.

## Selection and precedence

- **Manifest key** - `build.preset` holds the global preset. Absent or `none` means
  preset none.
- **Invocation escape** - `--preset <name>` on `releasegen build` and `releasegen run`
  overrides the Manifest key for that invocation only. The flag never writes the file:
  strict-manifest rules stay intact, and one-off releases stay cheap.
- **`init` seeding** - `releasegen init --preset <name>` writes the chosen preset into the
  skeleton's `build.preset` key; `none`, or omitting the flag, seeds no key.
- **Per-key overrides** - any explicit bool key in the build section (`publishAot`,
  `publishTrimmed`, `readyToRun`, `selfContained`, `singleFile`) beats the preset default. See the [manifest schema](manifest-schema.md).

## Single-file defaults

Single-file deployment is part of the preset surface:

- For the four named presets, single-file publishing defaults to **on on Unix-based OSes**,
  and **on Windows only when the target's resolved archive format is zip** - declared, or
  defaulted to zip. See the [single-file rules](single-file-rules.md) page for the
  incompatible-API reference behind the opt-out.
- Preset `none` contributes no implicit single-file switch.
- The `build.singleFile` key disables the defaults (opt-out) or forces them on; the
  explicit key always wins.

## See also

- [Manifest schema](manifest-schema.md) - the `build` section keys
- [Single-file rules](single-file-rules.md)
- [Command reference](commands.md) - the `--preset` flags
