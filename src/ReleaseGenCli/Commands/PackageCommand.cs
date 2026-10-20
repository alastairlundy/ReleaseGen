namespace ReleaseGenCli.Commands;

using System.CommandLine;
using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenLib;
using ReleaseGenLib.Models;

/// <summary>
/// The package stage: runs <c>dotnet pack -c Release -o &lt;output&gt;</c> once per declared
/// package kind per target, producing exactly the artifact kinds each manifest target declares
/// and nothing more. No inference from project file type: a <c>tool-package</c> is
/// produced by declaring the kind, not by inspecting the csproj, and every declared kind of
/// every target gets its own invocation even when several targets name the same project.
/// Output stays local under <c>release/packages/lib|tool/&lt;project-name&gt;/</c> -
/// nothing is uploaded here.
/// </summary>
/// <remarks>
/// The <c>executables</c> kind is a valid declaration whose release artifacts are the build
/// stage's product; this stage skips it rather than failing. Pack output is routed
/// through the conventions layer: stdout lines become stage notes and stderr lines
/// become warnings so they stay visible under <c>--quiet</c>.
/// </remarks>
public sealed class PackageCommand : Command
{
    public PackageCommand(IServiceProvider services) : base("package",
        "Packages each target's declared artifact kinds.")
    {
        SetAction(async (parseResult, cancellationToken) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, unknown
            // kind, failed pack - is caught here and rendered as the one-line error.
            try
            {
                ManifestLoader manifestLoader = services.GetRequiredService<ManifestLoader>();
                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                if (manifest.Targets.Count == 0)
                {
                    // An expected condition is a named report, not an exception.
                    CliOutput.Error("package: the manifest declares no targets; nothing to package.");
                    return 1;
                }

                DirectoryInfo outputRoot = new(Path.Combine(Environment.CurrentDirectory, "release", "packages"));
                PackageKindRunner kindRunner = new(services.GetRequiredService<IProcessInvoker>());

                int producedCount = 0;
                List<string> producedKinds = [];

                foreach (ManifestTarget target in manifest.Targets)
                {
                    FileInfo projectFile = ManifestLoader.ResolveProjectFile(
                        target.Project, manifestFile.Directory!.FullName);

                    foreach (string kind in target.Kinds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // The executables kind is the build stage's product, not a package
                        // stage output: skip it here, never fail on it.
                        if (string.Equals(kind, ArtifactKinds.Executables, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        await kindRunner.RunAsync(
                            projectFile,
                            kind,
                            outputRoot,
                            static line => CliOutput.Note(line),
                            static line => CliOutput.Warning(line),
                            cancellationToken);

                        producedCount++;

                        if (!producedKinds.Contains(kind))
                        {
                            producedKinds.Add(kind);
                        }
                    }
                }

                if (producedCount == 0)
                {
                    CliOutput.Note(
                        $"package: no package kinds declared across {manifest.Targets.Count} target(s); nothing to package.");
                }
                else
                {
                    CliOutput.Note(
                        $"package: produced {producedCount} release artifact(s) of kind(s) {string.Join(", ", producedKinds)} across {manifest.Targets.Count} target(s) under '{outputRoot.FullName}'.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                CliOutput.Error(ex.Message);
                return 1;
            }
        });
    }
}
