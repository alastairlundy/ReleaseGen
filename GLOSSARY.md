# Glossary

## Manifest

The root configuration file that drives every stage of a ReleaseGen run.

## Stage

One named phase of a ReleaseGen run, such as build, test, package, or publish.

## Chained run

A single ReleaseGen command that executes the stages named by the manifest in manifest order.

## Test gate

The rule that publishing blocks when the manifest carries a test section and that section's stage fails.

## Build preset

A named set of build settings (aot, trimming, ready-to-run, self-contained, and the single-file rules) applied to a rid's publish when the manifest or the user selects it.

## Target

A manifest entry naming one project and the artifacts it produces.

## Release artifact

Something a target produces: release binaries, lib package, or dotnet tool package.

## Archive format

The packaging ReleaseGen produces for a target's release binaries; zip, tar.gz, and tar.xz
are members of this set.

## Dry run

A mode in which every Stage runs its planning and read-only steps and reports what it would have done, with writes and outward effects suppressed.

## Package kind

What a target's package output is: a `nupkg` library package or a dotnet tool package. The kind drives the package stage's switches.
