# Manifest schema

The Manifest is the root configuration file that drives every stage of a ReleaseGen run.
In v1 it is exactly one JSON file per repository, named `releasegen.json`, parsed with
System.Text.Json. There is no per-solution manifest.

## Strictness

- **Unknown fields fail parse** with an error naming the field. A misspelled key fails
  loudly rather than silently dropping.
- **No inline comments** by design.
- **No secret ever lives here.** The Manifest has no key-bearing field, and a stray one fails
  the strict parse: API keys travel only through the `--api-key` option or the `NUGET_API_KEY`
  environment variable.
- The file is discovered by walking up from the current directory to the repository root,
  stopping at the first `releasegen.json`.

## Sections

Keys are organized one section per stage: `targets`, `build`, `test`, `publish`.

### Root keys

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `targets` | array | Yes (may be empty) | The Manifest's targets: projects and their declared artifact kinds. |
| `build` | object | Yes | The build section: preset, runtime identifiers, per-key flag overrides. |
| `test` | object | No | The test section. Absent means the test gate passes with a warning. |
| `publish` | object | Yes | The publish section: NuGet source URLs. |

### `targets` entries

Each target is a Manifest entry naming one project and the release artifacts it produces.
One project may be named by more than one target, so a single project can produce
multiple artifact kinds.

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `project` | string | Yes | Path to the project file or directory. Relative paths resolve from the Manifest's directory. |
| `kinds` | string[] | Yes | The artifact kinds this target declares. Only declared kinds are produced. |
| `archiveFormats` | string or string[] | No (default resolves per rid) | The archive formats for this target's executables-kind outputs: `zip`, `tar.gz`, `tar.xz`, or `none`. Accepts a bare string or a list, and each declared format produces one archive per (target, rid). Archiving is opt-out, not opt-in. Package kinds are never archived. |

`archiveFormats` accepts either JSON spelling, and both normalize to one list at parse time:

```json
"archiveFormats": "zip"
```

```json
"archiveFormats": ["zip", "tar.gz"]
```

Every list entry must be a known format; an unknown value fails validation with an error
naming the target, the value, and the known set. A token shape other than a string or a
string list (for example a number, an object, or an explicit `null`) fails parse naming the
key. The key was named `archive` (single string only) in earlier releases: that name is a
breaking rename and now fails strict parse like any unknown field.

### Absent-key defaults

When a target declares no `archiveFormats` key, the default set resolves from the rid class.
Archiving stays opt-out, and `tar.xz` is never a default on any platform:

| RID class | Example rids | Default `archiveFormats` |
|-----------|--------------|--------------------------|
| Windows-class | `win-x64`, `win-arm64` | `zip` |
| Portable `any-*` (carve-out) | `any-x64`, `any-arm64` | `zip` - the portable rids follow the Windows rule regardless of host OS |
| Unix-based | `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | `zip`, `tar.gz` |

An explicit `archiveFormats` key overrides this table for every rid of the target.
Each resolved format produces one archive per (target, rid).

`releasegen init` seeds the full default set explicitly for a target whose project is an
executable - `"archiveFormats": ["zip", "tar.gz"]` - so the default is visible in the written
file instead of hiding behind an absent key; trim the list to the rid class you actually
declare. `tar.xz` is never seeded. See the [command reference](commands.md#init).

### `tar.xz` and Windows-class or portable rids

`tar.xz` is opt-in only: neither Windows-class (`win-*`) nor portable (`any-*`) rids produce it.
Two outcomes follow when a manifest declares it:

- A target declaring `tar.xz` as its **only** format fails validation when the Manifest names a
  Windows-class or portable rid: the error names the target, the conflicting rids, and the fix -
  declare another format such as `zip`, or remove those runtime identifiers.
- A target declaring `tar.xz` **alongside** another format (for example `["zip", "tar.xz"]`)
  validates; the build stage skips `tar.xz` for each Windows-class or portable rid and prints
  one warning per skipped (target, rid) pair - see the [command reference](commands.md#build).
  Unix-based rids in the same target are unaffected by the skip.

The artifact kind vocabulary is exactly:

| Kind | Produced by |
|------|-------------|
| `executables` | The build stage: release binaries and their archives. |
| `lib-package` | The package stage: a library NuGet package. |
| `tool-package` | The package stage: a dotnet tool NuGet package. |

ReleaseGen evaluates each target's project file with MSBuild to read its **expanded**
properties - never raw project text - and cross-checks them against the declared kinds:

- A target declaring `executables` whose project evaluates to a library output fails Manifest
  validation, naming the target and the mismatch.
- A target declaring `tool-package` whose project does not evaluate to `PackAsTool=true` fails
  Manifest validation naming the fix. The property is read expanded, so a declaration in a
  `Directory.Build.props` or any other imported props file counts exactly like one in the
  project file, and the message names the declaration site generically rather than assuming
  the project file:

```
error: Manifest validation failed: the target 'src/MyCli/MyCli.csproj' declares the 'tool-package' kind but MSBuild evaluation of '...MyCli.csproj' reports PackAsTool '(unset)' (expected 'true'). Fix: set PackAsTool to true where the project's MSBuild properties are declared - the project file or an imported props file such as Directory.Build.props. ReleaseGen reads the expanded property and never passes the switch itself.
```

ReleaseGen never passes `PackAsTool` as a build switch: the project file stays authoritative,
and a project not configured as a dotnet tool can only be fixed there.

### `build` section

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `preset` | string | No | The global build preset key. One of the four [preset names](presets.md); absent or `none` means preset none. |
| `runtimeIdentifiers` | string[] | Yes | The rid list: the build stage publishes once per rid with the Release configuration. |
| `publishAot` | bool | No | Per-key override: publish AOT. |
| `publishTrimmed` | bool | No | Per-key override: publish trimmed. |
| `readyToRun` | bool | No | Per-key override: publish ready-to-run. |
| `selfContained` | bool | No | Per-key override: self-contained deployment. |
| `singleFile` | bool | No | The single-file opt-out key: `false` disables the single-file defaults; absent leaves the OS rules in force. |

The bool keys are tri-state: absent means the Manifest does not override the preset default.
An explicit key always beats the selected preset. See [build presets](presets.md) and
[single-file rules](single-file-rules.md).

### `test` section

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `projects` | string[] | Yes | The named test projects. Empty means the test stage runs `dotnet test` from the current directory. |

Declaring this section arms the test gate: publishing blocks when the test
stage exits nonzero. Omitting it makes the gate pass by default with a warning.

### `publish` section

| Key | Type | Required | Meaning |
|-----|------|----------|---------|
| `sources` | string[] | Yes | The NuGet source URLs to publish to. Empty means the nuget.org default. A blank entry fails with an error naming its position. |

Sources only - credentials never live on disk.

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
      "archiveFormats": ["zip", "tar.gz"]
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

`releasegen init` writes a minimal valid skeleton in exactly this shape.

## See also

- [Build presets](presets.md)
- [Single-file rules](single-file-rules.md)
- [Command reference](commands.md)
- [Stage handoff contract](stage-handoff.md)
