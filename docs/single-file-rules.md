# Single-file rules

Single-file deployment bundles an app's dependencies into one executable. ReleaseGen applies
single-file defaults per the build preset rules, exposes the opt-out key, and
surfaces the .NET SDK's incompatible-API diagnostics - ReleaseGen performs no IL-level
detection of its own.

## When single-file applies

- For the four named [build presets](presets.md), single-file defaults **on** on Unix-based OSes,
  and **on Windows only when that rid's resolved archive format is zip** - declared through
  the target's `archiveFormats` key (a list counts when it carries `zip` anywhere), or
  defaulted to zip. The absent-key default resolves per rid class: zip for Windows-class and
  portable `any-*` rids; zip plus tar.gz for Unix-based rids. Every default set includes zip,
  so an undeclared key is always zip-archived - see the [manifest schema](manifest-schema.md).
- Archiving is opt-out, not opt-in: a target with no `archiveFormats` key still archives by
  the per-rid defaults above, and each declared format produces one archive per (target, rid).
  The single-file tie-in only asks whether any of those formats is zip.
- Preset `none` contributes no implicit single-file switch; the project file's own
  `PublishSingleFile` setting stays authoritative.
- The `build.singleFile` key in the Manifest is the opt-out: `"singleFile": false` disables
  the defaults; `"singleFile": true` forces them on. The explicit key always wins over the OS
  rules.

## Incompatible APIs

The most common cause of single-file problems is dependence on file paths for files or DLLs
shipped with the application. The .NET runtime documents these API incompatibilities for
single-file apps:

| API | Note |
|-----|------|
| `Assembly.CodeBase` | Throws `PlatformNotSupportedException`. |
| `Assembly.EscapedCodeBase` | Throws `PlatformNotSupportedException`. |
| `Assembly.GetFile` | Throws `IOException`. |
| `Assembly.GetFiles` | Throws `IOException`. |
| `Assembly.Location` | Returns an empty string. |
| `AssemblyName.CodeBase` | Returns `null`. |
| `AssemblyName.EscapedCodeBase` | Returns `null`. |
| `Module.FullyQualifiedName` | Returns a string with the value of `<Unknown>` or throws an exception. |
| `Module.Name` | Returns a string with the value of `<Unknown>`. |
| `Marshal.GetHINSTANCE` | Returns -1. |

Related behavior changes for single-file publishing (.NET 5 and later): bundled assemblies
are loaded from memory instead of extracted to disk, `AppContext.BaseDirectory` is the
containing directory of the host executable, and `Environment.GetCommandLineArgs()[0]` is the
name of the host executable. To access files next to the executable, use
`AppContext.BaseDirectory`.

## ReleaseGen's handling

- The SDK's IL3000-family single-file analyzer warnings surface during publish as console
  warnings, so they stay visible under `--quiet`.
- ReleaseGen itself performs no IL-level detection; the table above is the
  documentation backing the opt-out, not a ReleaseGen check.
- If your project (or a package it uses) depends on an incompatible API, set
  `"singleFile": false` in the Manifest's build section and the single-file defaults are
  disabled.
- Apps that resolve the .NET SDK at runtime (for example through
  `Microsoft.Build.Locator`, as ReleaseGen itself does) are incompatible with
  **self-contained single-file** publishing: the bundled runtime's hostfxr cannot discover
  an installed SDK, so MSBuild evaluation fails at runtime. Framework-dependent single-file
  builds still work provided the target machine has the .NET SDK on `PATH`/`DOTNET_ROOT`.
  Set `"singleFile": false` for such targets, or keep them framework-dependent.

## Sources

This page's incompatible-API reference is sourced from the official .NET documentation:

- [Single-file deployment - API incompatibility](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview#other-considerations)
- [Assembly-related API behavior changes for single-file publishing format](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/5.0/assembly-api-behavior-changes-for-single-file-publish)

## See also

- [Build presets](presets.md)
- [Manifest schema](manifest-schema.md) - the `singleFile` key
