# Glossary

## Manifest

The root configuration file that drives every stage of a ReleaseGen run.

## Stage

One named phase of a ReleaseGen run, such as build, test, package, or publish.

## Chained run

A single ReleaseGen command that executes the stages named by the manifest in manifest order.

## Test gate

The rule that publishing blocks when the manifest carries a test section and that section's stage fails.

## Target

A manifest entry naming one project and the artifacts it produces.

## Release artifact

Something a target produces: release binaries, lib package, or dotnet tool package.
