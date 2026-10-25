/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

using ReleaseGen.Cli.Localizations;

namespace ReleaseGen.Cli.Commands;

/// <summary>
/// The package stage: runs <c>dotnet pack -c Release -o &lt;output&gt;</c> once per declared
/// package kind per target, producing exactly the artefact kinds each manifest target declares
/// and nothing more. No inference from project file type: a <c>tool-package</c> is
/// produced by declaring the kind, not by inspecting the csproj, and every declared kind of
/// every target gets its own invocation even when several targets name the same project.
/// <c>--dry-run</c> prints each planned pack through the shared runner and writes nothing.
/// A real run under <c>--verbose</c> shows each pack's command line, raw output, and cause
/// line, and <c>--quiet</c> together with <c>--verbose</c> fails with the conflict error
/// before the stage starts.
/// Output stays local under <c>release/packages/lib|tool/&lt;project-name&gt;/</c> -
/// nothing is uploaded here.
/// </summary>
/// <remarks>
/// The <c>executables</c> kind is a valid declaration whose release artefacts are the build
/// stage's product; this stage skips it rather than failing. Pack output is routed
/// through the conventions layer: stdout lines become stage notes and stderr lines
/// become warnings so they stay visible under <c>--quiet</c>.
/// </remarks>
public sealed class PackageCommand : Command
{
    public PackageCommand(IManifestLoader manifestLoader, IProcessInvoker processInvoker) : base("package",
        "Packages each target's declared artefact kinds.")
    {
        Option<bool> dryRunOption = CliOptions.CreateDryRun();
        Option<bool> verboseOption = CliOptions.CreateVerbose();
        Add(dryRunOption);
        Add(verboseOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliOutput.ApplyOutputModes(parseResult, verboseOption))
            {
                return 1;
            }

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, unknown
            // kind, failed pack - is caught here and rendered as the one-line error.
            try
            {
                // One decision point for the runner: the flag.
                StageRunMode runMode = parseResult.GetValue(dryRunOption)
                    ? StageRunMode.DryRun
                    : StageRunMode.Execute;

                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                if (manifestFile.Directory is null || !Directory.Exists(manifestFile.Directory.FullName))
                {
                    CliOutput.Error(Resources.Exceptions_ManifestFile_NotFound);
                    return 1;
                }

                string directoryPath = manifestFile.Directory.FullName;

                if (manifest.Targets.Count == 0)
                {
                    // An expected condition is a named report, not an exception.
                    CliOutput.Error("package: the manifest declares no targets; nothing to package.");
                    return 1;
                }

                
                DirectoryInfo outputRoot = new(Path.Combine(Environment.CurrentDirectory, "release", "packages"));
                PackageKindRunner packageCommandRunner = new(processInvoker);

                int producedCount = 0;
                List<string> producedKinds = [];

                foreach (ManifestTarget target in manifest.Targets)
                {
                    FileInfo projectFile = manifestLoader.ResolveProjectFile(
                        target.Project, directoryPath);

                    producedCount +=  await CreatePackage(target, packageCommandRunner, projectFile,
                            outputRoot, producedKinds, runMode, cancellationToken)
                        .ConfigureAwait(true);
                }

                // The dry run planned every pack above without spawning it, so its summary
                // reports plans rather than artefacts it did not produce.
                CliOutput.Note(
                    producedCount == 0
                        ? $"package: no package kinds declared across {manifest.Targets.Count} target(s); nothing to package."
                        : runMode == StageRunMode.DryRun
                            ? $"package: dry run: planned {producedCount} pack invocation(s) of kind(s) {string.Join(", ", producedKinds)} across {manifest.Targets.Count} target(s); nothing was written under '{outputRoot.FullName}'."
                            : $"package: produced {producedCount} release artifact(s) of kind(s) {string.Join(", ", producedKinds)} across {manifest.Targets.Count} target(s) under '{outputRoot.FullName}'.");

                return 0;
            }
            catch (Exception ex)
            {
                CliOutput.Error(ex.Message);
                return 1;
            }
        });
    }

    private static async Task<int> CreatePackage(ManifestTarget target, PackageKindRunner packageCommandRunner, FileInfo projectFile,
        DirectoryInfo outputRoot, List<string> producedKinds, StageRunMode runMode, CancellationToken cancellationToken)
    {
        int producedCount = 0;
        
        foreach (string targetKind in target.Kinds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The executables kind is the build stage's product, not a package
            // stage output: skip it here, never fail on it.
            if (string.Equals(targetKind, ArtifactKinds.Executables, StringComparison.Ordinal))
            {
                continue;
            }

            await packageCommandRunner.RunAsync(
                projectFile, targetKind, outputRoot,
                static line => CliOutput.Note(line),
                static line => CliOutput.Warning(line),
                CliOutput.Cause,
                runMode,
                cancellationToken).ConfigureAwait(true);

            producedCount++;

            if (!producedKinds.Contains(targetKind, StringComparer.OrdinalIgnoreCase))
            {
                producedKinds.Add(targetKind);
            }
        }

        return producedCount;
    }
}