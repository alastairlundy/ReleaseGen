# Manifest schema

The Manifest is the root configuration file that drives every stage of a ReleaseGen run
(D005). In v1 it is exactly one JSON file per repository, named `releasegen.json`, parsed with
System.Text.Json (T003). There is no per-solution manifest (D002).

## Strictness

- **Unknown fields fail parse** with an error naming the field (D002). A misspelled key fails
  loudly rather than silently dropping.
- **No inline comments** by design (T003).
- **No secret ever lives here.** The Manifest has no key-bearing field, and a stray one fails
  the strict parse: API keys travel only through the `--api-key` option or the `NUGET_API_KEY`
  environment variable (D011).
- The file is discovered by walking up from the current directory to the repository root,
  stopping at the first `releasegen.json` (D009).

## Sections

Keys are organized one section per stage (T007): `targets`, `build`, `test`, `publish`.

### Root keys

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `targets` | array | Yes (may be empty) | The Manifest's targets: projects and their declared artifact kinds. |
| `build` | object | Yes | The build section: preset, runtime identifiers, per-key flag overrides. |
| `test` | object | No | The test section. Absent means the test gate passes with a warning (D004). |
| `publish` | object | Yes | The publish section: NuGet source URLs. |

### `targets` entries

Each target is a Manifest entry naming one project and the release artifacts it produces
(D014). One project may be named by more than one target, so a single project can produce
multiple artifact kinds (D013).

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `project` | string | Yes | Path to the project file or directory. Relative paths resolve from the Manifest's directory. |
| `kinds` | string[] | Yes | The artifact kinds this target declares. Only declared kinds are produced (D013). |
| `archive` | string | No (default `zip`) | The archive format for this target's executables-kind outputs: `zip`, `tar.gz`, or `none`. Archiving is opt-out, not opt-in (T016). Package kinds are never archived. |

The artifact kind vocabulary (D015) is exactly:

| Kind | Produced by |
|------|-------------|
| `executables` | The build stage: release binaries and their archives. |
| `lib-package` | The package stage: a library NuGet package. |
| `tool-package` | The package stage: a dotnet tool NuGet package. |

ReleaseGen evaluates each target's project file with MSBuild to read its `OutputType` and
cross-checks it against the declared kinds: a target declaring `executables` whose project
evaluates to a library output fails Manifest validation, naming the target and the mismatch
(D020).

### `build` section

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `preset` | string | No | The global build preset key (T013). One of the four [preset names](presets.md); absent or `none` means preset none. |
| `runtimeIdentifiers` | string[] | Yes | The rid list: the build stage publishes once per rid with the Release configuration (T009). |
| `publishAot` | bool | No | Per-key override: publish AOT. |
| `publishTrimmed` | bool | No | Per-key override: publish trimmed. |
| `readyToRun` | bool | No | Per-key override: publish ready-to-run. |
| `selfContained` | bool | No | Per-key override: self-contained deployment. |
| `singleFile` | bool | No | The single-file opt-out key (T014): `false` disables the single-file defaults; absent leaves the OS rules in force. |

The bool keys are tri-state: absent means the Manifest does not override the preset default.
An explicit key always beats the selected preset (T013). See [build presets](presets.md) and
[single-file rules](single-file-rules.md).

### `test` section

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `projects` | string[] | Yes | The named test projects. Empty means the test stage runs `dotnet test` from the current directory (T011). |

Declaring this section arms the test gate (D004, D008): publishing blocks when the test
stage exits nonzero. Omitting it makes the gate pass by default with a warning.

### `publish` section

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `sources` | string[] | Yes | The NuGet source URLs to publish to. Empty means the nuget.org default (D011). A blank entry fails with an error naming its position. |

Sources only - credentials never live on disk (D011).

## Example

```json
{
  "targets": [
    {
      "project": "src/MyLib/MyLib.csproj",
      "kinds": ["lib-package"]
    },
    {
      "project": "src/MyCli/MyCli.csproj",
      "kinds": ["executables", "tool-package"],
      "archive": "zip"
    }
  ],
  "build": {
    "preset": "trimming-only",
    "runtimeIdentifiers": ["win-x64", "linux-x64", "osx-arm64"],
    "singleFile": true
  },
  "test": {
    "projects": ["src/MyLib.Tests/MyLib.Tests.csproj"]
  },
  "publish": {
    "sources": []
  }
}
```

`releasegen init` writes a minimal valid skeleton in exactly this shape (T008).

## See also

- [Build presets](presets.md)
- [Single-file rules](single-file-rules.md)
- [Command reference](commands.md)
- [Stage handoff contract](stage-handoff.md)
