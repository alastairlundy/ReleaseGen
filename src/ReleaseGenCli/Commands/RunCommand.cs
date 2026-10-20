namespace ReleaseGenCli.Commands;

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenLib;
using ReleaseGenLib.Models;

/// <summary>
/// The chained run: <c>releasegen run</c> executes the stages the manifest names in manifest
/// order (D010), aborting the chain at the first failed stage with a one-line error naming it
/// and its exit code (D009, D010), and reporting each skipped unnamed stage with a console
/// note. Each stage runs through its own stage command invoked in-process (D003 - the stage
/// commands remain the separately invocable implementations), so the chained run and separate
/// stage invocations share one behavior surface and write the identical <c>release/</c>
/// artifact tree documented by the cross-process handoff contract (D003, D018). init is a
/// one-shot command and takes no part in the chain (D003). The <c>--preset</c> flag overrides
/// the manifest's <c>build.preset</c> for this invocation only and never writes the file
/// (T013); stage flags and retry logic stay out of v1 (D010).
/// </summary>
/// <remarks>
/// Stage selection follows the manifest: build always runs; test is named by a declared test
/// section (D004); package and publish are named by targets declaring package kinds (D013).
/// The test stage's gate outcome is recorded in <see cref="StageContext"/> (D004, D010) so the
/// publish stage consumes it instead of re-running tests inside the same chain.
/// </remarks>
public sealed class RunCommand : Command
{
    public RunCommand(IServiceProvider services) : base("run",
        "Runs the manifest's stages in manifest order (chained run).")
    {
        Option<string?> presetOption = new("--preset")
        {
            Description = "Override the manifest's build.preset for this invocation only (T013). Does not write the manifest.",
        };

        Add(presetOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, refused
            // preset, failed stage - is caught here and rendered as D009's one-line error.
            try
            {
                // T013 precedence: --preset beats the manifest key for this invocation only.
                string? flagPreset = parseResult.GetValue(presetOption);

                if (!string.IsNullOrWhiteSpace(flagPreset) && !BuildPreset.TryParse(flagPreset, out _))
                {
                    CliOutput.Error(
                        $"Unknown preset '{flagPreset}'. Known presets: {string.Join(", ", BuildPreset.All)}.");
                    return 1;
                }

                ManifestLoader manifestLoader = services.GetRequiredService<ManifestLoader>();
                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                List<string> presetArguments = string.IsNullOrWhiteSpace(flagPreset)
                    ? []
                    : ["--preset", flagPreset];

                List<StagePlan> stages =
                [
                    // Build is always named: every stage reads the same manifest (D003).
                    new("build", Included: true, SkipReason: null, Arguments: presetArguments),

                    // The test stage is named by a declared test section (D004).
                    new("test",
                        Included: manifest.Test is not null,
                        SkipReason: "the manifest declares no test section (D004)",
                        Arguments: []),

                    // Package and publish are named by targets declaring package kinds (D013).
                    new("package",
                        Included: DeclaresPackageKinds(manifest),
                        SkipReason: "no target declares a package kind (D013)",
                        Arguments: []),
                    new("publish",
                        Included: DeclaresPackageKinds(manifest),
                        SkipReason: "no target declares a package kind (D013)",
                        Arguments: []),
                ];

                CliOutput.Note(
                    $"run: chained run starting; stages in manifest order: {string.Join(", ", stages.Where(static s => s.Included).Select(static s => s.Name))} (D010).");

                // A stale in-process gate outcome must never leak into this chain (D004).
                StageContext.Reset();

                bool quiet = CliOutput.Quiet;
                int executedCount = 0;

                foreach (StagePlan stage in stages)
                {
                    if (!stage.Included)
                    {
                        // D010 skip notes: the executed stage set stays honest on the console.
                        CliOutput.Note($"run: skipping the {stage.Name} stage; {stage.SkipReason} (D010).");
                        continue;
                    }

                    int exitCode = await InvokeStageAsync(
                        services, stage.Name, stage.Arguments, quiet, cancellationToken);

                    if (exitCode != 0)
                    {
                        // D010 fail-fast: the first failed stage aborts the chain, so later
                        // stages - notably publish behind a failed test stage (D004) - never run.
                        CliOutput.Error(
                            $"run: the {stage.Name} stage failed (exit code {exitCode}); aborting the run before the remaining stages (D010).");
                        return exitCode;
                    }

                    if (string.Equals(stage.Name, "test", StringComparison.Ordinal))
                    {
                        // D004 gate handoff: the chain already ran the test stage, so the
                        // publish stage consults this outcome instead of re-running it.
                        StageContext.TestGate = TestGateResult.FromStage(exitCode, hasTestSection: true);
                    }

                    executedCount++;
                }

                CliOutput.Note($"run: {executedCount} stage(s) completed successfully.");
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
    /// Returns whether any target declares a package kind (D013), which names the package and
    /// publish stages for the chain; executables-kind-only manifests have nothing to package or
    /// push.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    private static bool DeclaresPackageKinds(Manifest manifest) =>
        manifest.Targets.Any(static target =>
            target.Kinds.Any(static kind =>
                kind is ArtifactKinds.LibPackage or ArtifactKinds.ToolPackage));

    /// <summary>
    /// Runs one stage in-process through its stage command (D003), so the chain and a separate
    /// invocation of the same stage share one behavior surface. The stage re-resolves the
    /// manifest itself; <c>--quiet</c> (and, for build, <c>--preset</c>) forward so the stage
    /// sees the chain's invocation flags (T013, T017).
    /// </summary>
    /// <param name="services">The DI container the stage commands resolve from.</param>
    /// <param name="stageName">The stage command to invoke: build, test, package, or publish.</param>
    /// <param name="extraArguments">Extra stage flags for this invocation, such as the preset override.</param>
    /// <param name="quiet">Whether the chain runs under <c>--quiet</c>; forwarded to the stage.</param>
    /// <param name="cancellationToken">The chain's cancellation token.</param>
    /// <returns>The stage command's exit code.</returns>
    private static async Task<int> InvokeStageAsync(
        IServiceProvider services,
        string stageName,
        IReadOnlyList<string> extraArguments,
        bool quiet,
        CancellationToken cancellationToken)
    {
        Command stageCommand = stageName switch
        {
            "build" => new BuildCommand(services),
            "test" => new TestCommand(services),
            "package" => new PackageCommand(services),
            "publish" => new PublishCommand(services),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stageName), stageName, "run: unknown stage name."),
        };

        RootCommand temporaryRoot = new("ReleaseGen stage invocation root.");
        temporaryRoot.Add(stageCommand);
        temporaryRoot.Add(CliOptions.Quiet);

        List<string> argv = [stageName, .. extraArguments];

        if (quiet)
        {
            argv.Add("--quiet");
        }

        ParseResult result = temporaryRoot.Parse(argv);

        return await result.InvokeAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// One planned chain stage: whether the manifest names it (D010), why it is skipped when
    /// not, and the extra stage flags the chain forwards (T013).
    /// </summary>
    /// <param name="Name">The stage command name.</param>
    /// <param name="Included">Whether the manifest names this stage.</param>
    /// <param name="SkipReason">The console-note reason the stage is skipped, or null when included.</param>
    /// <param name="Arguments">Extra stage flags for this invocation.</param>
    private sealed record StagePlan(string Name, bool Included, string? SkipReason, List<string> Arguments);
}
