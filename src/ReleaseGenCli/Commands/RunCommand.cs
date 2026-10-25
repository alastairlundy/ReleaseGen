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
/// The chained run: <c>releasegen run</c> executes the stages the manifest names in manifest
/// order, aborting the chain at the first failed stage with a one-line error naming it
/// and its exit code, and reporting each skipped unnamed stage with a console
/// note. Each stage runs through its own stage command invoked in-process (the stage commands
/// remain the separately invocable implementations), so the chained run and separate
/// stage invocations share one behaviour surface and write the identical <c>release/</c>
/// artefact tree documented by the cross-process handoff contract. init is a
/// one-shot command and takes no part in the chain. The <c>--preset</c> flag overrides
/// the manifest's <c>build.preset</c> for this invocation only and never writes the file;
/// <c>--dry-run</c> forwards to every stage, so the chain prints each stage's plan stage by
/// stage and writes nothing anywhere; <c>--verbose</c> forwards for the same reason, so each
/// named stage prints its command lines, raw output, and cause lines, and passing it together
/// with <c>--quiet</c> fails the chain before the first stage starts. Retry logic stays out of v1.
/// </summary>
/// <remarks>
/// Stage selection follows the manifest: build always runs; test is named by a declared test
/// section; package and publish are named by targets declaring package kinds.
/// The test stage's gate outcome is recorded in <see cref="StageContext"/> so the
/// publish stage consumes it instead of re-running tests inside the same chain.
/// </remarks>
public sealed class RunCommand : Command
{
    private readonly IManifestLoader _manifestLoader;
    private readonly IProcessInvoker _processInvoker;
    private readonly IBuildStageRunner _buildStageRunner;

    public RunCommand(IManifestLoader manifestLoader, IProcessInvoker processInvoker, IBuildStageRunner buildStageRunner) : base("run",
        "Runs the manifest's stages in manifest order (chained run).")
    {
        _manifestLoader = manifestLoader;
        _processInvoker = processInvoker;
        _buildStageRunner = buildStageRunner;
        Option<string?> presetOption = new("--preset")
        {
            Description = "Override the manifest's build.preset for this invocation only. Does not write the manifest.",
        };

        Option<bool> dryRunOption = CliOptions.CreateDryRun();
        Option<bool> verboseOption = CliOptions.CreateVerbose();

        Add(presetOption);
        Add(dryRunOption);
        Add(verboseOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliOutput.ApplyOutputModes(parseResult, verboseOption))
            {
                return 1;
            }

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, refused
            // preset, failed stage - is caught here and rendered as the one-line error.
            try
            {
                // Preset precedence: --preset beats the manifest key for this invocation only.
                string? flagPreset = parseResult.GetValue(presetOption);
                bool dryRun = parseResult.GetValue(dryRunOption);
                bool verbose = parseResult.GetValue(verboseOption);

                if (!string.IsNullOrWhiteSpace(flagPreset) && !BuildPreset.TryParse(flagPreset, out _))
                {
                    CliOutput.Error(
                        $"Unknown preset '{flagPreset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
                    return 1;
                }

                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                List<string> presetArguments = string.IsNullOrWhiteSpace(flagPreset)
                    ? []
                    : ["--preset", flagPreset];

                List<StagePlan> stages =
                [
                    // Build is always named: every stage reads the same manifest.
                    new("build", Included: true, SkipReason: null, Arguments: presetArguments),

                    // The test stage is named by a declared test section.
                    new("test",
                        Included: manifest.Test is not null,
                        SkipReason: "the manifest declares no test section",
                        Arguments: []),

                    // Package and publish are named by targets declaring package kinds.
                    new("package",
                        Included: DeclaresPackageKinds(manifest),
                        SkipReason: "no target declares a package kind",
                        Arguments: []),
                    new("publish",
                        Included: DeclaresPackageKinds(manifest),
                        SkipReason: "no target declares a package kind",
                        Arguments: []),
                ];

                CliOutput.Note(
                    dryRun
                        ? $"run: dry run: chained plan starting; stages in manifest order: {string.Join(", ", stages.Where(static s => s.Included).Select(static s => s.Name))}."
                        : $"run: chained run starting; stages in manifest order: {string.Join(", ", stages.Where(static s => s.Included).Select(static s => s.Name))}.");

                // A stale in-process gate outcome must never leak into this chain.
                StageContext.Reset();

                bool quiet = CliOutput.Quiet;
                int executedCount = 0;

                foreach (StagePlan stage in stages)
                {
                    if (!stage.Included)
                    {
                        // Skip notes: the executed stage set stays honest on the console.
                        CliOutput.Note($"run: skipping the {stage.Name} stage; {stage.SkipReason}.");
                        continue;
                    }

                    int exitCode = await InvokeStageAsync(
                        stage.Name, stage.Arguments, quiet, dryRun, verbose, cancellationToken).ConfigureAwait(true);

                    if (exitCode != 0)
                    {
                        // Fail-fast: the first failed stage aborts the chain, so later
                        // stages - notably publish behind a failed test stage - never run.
                        CliOutput.Error(
                            $"run: the {stage.Name} stage failed (exit code {exitCode}); aborting the run before the remaining stages.");
                        return exitCode;
                    }

                    if (!dryRun && string.Equals(stage.Name, "test", StringComparison.Ordinal))
                    {
                        // Gate handoff: the chain already ran the test stage, so the
                        // publish stage consults this outcome instead of re-running it.
                        // A dry run executed no tests, so it records no outcome - its
                        // publish stage prints the would-be consult from the manifest.
                        StageContext.TestGate = TestGateResult.FromStage(exitCode, hasTestSection: true);
                    }

                    executedCount++;
                }

                CliOutput.Note(
                    dryRun
                        ? $"run: dry run: {executedCount} stage(s) planned; nothing was written anywhere."
                        : $"run: {executedCount} stage(s) completed successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                CliOutput.Error(ex.Message);
                return 1;
            }
        });
    }

    /// <summary>
    /// Returns whether any target declares a package kind, which names the package and
    /// publish stages for the chain; executables-kind-only manifests have nothing to package or
    /// push.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    private static bool DeclaresPackageKinds(Manifest manifest) =>
        manifest.Targets.Any(static target =>
            target.Kinds.Any(static kind =>
                kind is ArtifactKinds.LibPackage or ArtifactKinds.ToolPackage));

    /// <summary>
    /// Runs one stage in-process through its stage command, so the chain and a separate
    /// invocation of the same stage share one behavior surface. The stage re-resolves the
    /// manifest itself; <c>--quiet</c>, <c>--dry-run</c>, <c>--verbose</c> (and, for build,
    /// <c>--preset</c>) forward so the stage sees the chain's invocation flags.
    /// </summary>
    /// <param name="stageName">The stage command to invoke: build, test, package, or publish.</param>
    /// <param name="extraArguments">Extra stage flags for this invocation, such as the preset override.</param>
    /// <param name="quiet">Whether the chain runs under <c>--quiet</c>; forwarded to the stage.</param>
    /// <param name="dryRun">Whether the chain runs under <c>--dry-run</c>; forwarded to the stage.</param>
    /// <param name="verbose">Whether the chain runs under <c>--verbose</c>; forwarded to the stage.</param>
    /// <param name="cancellationToken">The chain's cancellation token.</param>
    /// <returns>The stage command's exit code.</returns>
    private async Task<int> InvokeStageAsync(
        string stageName,
        IReadOnlyList<string> extraArguments,
        bool quiet,
        bool dryRun,
        bool verbose,
        CancellationToken cancellationToken)
    {
        Command stageCommand = stageName switch
        {
            "build" => new BuildCommand(_manifestLoader, _buildStageRunner),
            "test" => new TestCommand(_manifestLoader, _processInvoker),
            "package" => new PackageCommand(_manifestLoader, _processInvoker),
            "publish" => new PublishCommand(_manifestLoader, _processInvoker),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stageName), stageName, Resources.Exceptions_Stages_UnknownStageName),
        };

        RootCommand temporaryRoot = new("ReleaseGen stage invocation root.")
        {
            stageCommand,
            CliOptions.Quiet,
        };

        List<string> argv = [stageName, .. extraArguments];

        if (quiet)
        {
            argv.Add("--quiet");
        }

        // The flag reaches each stage the same way a user would pass it, so the chain and a
        // separate invocation of the stage share one behaviour surface.
        if (dryRun)
        {
            argv.Add("--dry-run");
        }

        if (verbose)
        {
            argv.Add("--verbose");
        }

        ParseResult result = temporaryRoot.Parse(argv);

        return await result.InvokeAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// One planned chain stage: whether the manifest names it, why it is skipped when
    /// not, and the extra stage flags the chain forwards.
    /// </summary>
    /// <param name="Name">The stage command name.</param>
    /// <param name="Included">Whether the manifest names this stage.</param>
    /// <param name="SkipReason">The console-note reason the stage is skipped, or null when included.</param>
    /// <param name="Arguments">Extra stage flags for this invocation.</param>
    private sealed record StagePlan(string Name, bool Included, string? SkipReason, List<string> Arguments);
}
