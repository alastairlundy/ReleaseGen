/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

using ReleaseGen.Cli.Exceptions;
using ReleaseGen.Cli.Localizations;
using ReleaseGen.Cli.Resolvers;

namespace ReleaseGen.Cli.Commands;

/// <summary>
/// The publishing stage: pushes each target's declared package kinds with
/// <c>dotnet nuget push</c> through CliInvoke, in manifest target order, to the
/// resolved sources. The API key arrives only through the <c>--api-key</c> option or the
/// <see cref="ApiKeyEnvironmentVariable"/> environment variable - never the manifest, whose
/// strict parse rejects any key-bearing field outright. Before the first push the
/// stage consults the test gate: a blocked outcome stops the stage with an error before
/// any network call, and an absent test section proceeds with a warning. Failure detail is
/// textual, not structured: the first failing push aborts the stage with a
/// one-line error naming the package, the source, and the exit code. Duplicate handling
/// resolves once per invocation from the manifest's <c>publish.skipDuplicates</c> key and the
/// <c>--skip-duplicate</c> switch: an enabled decision appends the switch to every push and
/// leaves duplicate judgment to the source - no pre-check of any kind - while a push whose
/// output carries the skip marker counts as skipped rather than failed, and the closing
/// summary reports pushed and skipped separately with the stage still exiting 0. Under
/// <c>--dry-run</c> the
/// stage plans instead: it prints the would-be gate consult (the test projects the gate would
/// run) and the would-be pushes, runs no tests, writes no <c>TestResults/</c>, and exits on
/// manifest validation alone. A real run under <c>--verbose</c> shows each push's command line
/// (key redacted), its raw output, and its cause line, while <c>--quiet</c> together with
/// <c>--verbose</c> fails with the conflict error before the stage starts.
/// </summary>
/// <remarks>
/// Packages are the package stage's local product: each declared package kind maps to
/// <c>release/packages/lib|tool/&lt;project-name&gt;/*.nupkg</c> under the invocation
/// directory, and the full push list is collected before the first push so a missing package
/// fails the stage with no partial upload. The executables kind is the build stage's product,
/// not a NuGet package, and is skipped here, never pushed. Push output reaches the
/// console through the conventions layer - stdout lines become stage notes and stderr
/// lines become warnings - and the streamed-args note redacts the API key so the key never
/// reaches the console. The dry-run plan names each kind's package directory rather than the
/// concrete <c>.nupkg</c> files a real run enumerates, because a plan may run before the
/// package stage has produced any.
/// </remarks>
public sealed class PublishCommand : Command
{
    private readonly IManifestLoader _manifestLoader;
    private readonly IProcessInvoker _processInvoker;
    private readonly PlannedCommandRunner _plannedCommandRunner;

    /// <summary>
    /// The environment-variable channel for the API key, consulted when the
    /// <c>--api-key</c> option carries no value.
    /// </summary>
    private const string ApiKeyEnvironmentVariable = "NUGET_API_KEY";

    /// <summary>
    /// The fixture-pinned skip marker: the exact text <c>dotnet nuget push --skip-duplicate</c>
    /// prints when the source declares a package already published. Both output streams are
    /// scanned for it with a strict ordinal match, so a matched push counts as skipped and
    /// never as a failed or warned one. The wording is pinned from
    /// NuGet's own duplicate message - "Package '&lt;path&gt;' already exists at feed
    /// '&lt;source&gt;'" - and the checked-in capture in
    /// src/ReleaseGen.Tests/Fixtures/nuget-push-duplicate.txt (a real nuget.org duplicate
    /// push, run 41900746380) matches this constant verbatim, with the notice on stdout.
    /// </summary>
    public const string SkipDuplicateMarker = "already exists at feed";

    public PublishCommand(IManifestLoader manifestLoader, IProcessInvoker processInvoker) : base("publish",
        "Publishes each target's declared package kinds to the configured NuGet sources.")
    {
        _manifestLoader = manifestLoader;
        _processInvoker = processInvoker;
        _plannedCommandRunner = new PlannedCommandRunner(processInvoker);
        Option<string?> apiKeyOption = new("--api-key")
        {
            Description =
                $"The NuGet API key. Falls back to the {ApiKeyEnvironmentVariable} environment variable. Never read from the manifest.",
        };

        // Absent parses to null, so the merge can tell "never asked" from an explicit
        // false; the switch alone can only ever enable skipping for one invocation.
        Option<bool?> skipDuplicateOption = new("--skip-duplicate")
        {
            Description =
                "Skip a push the source declares a duplicate instead of failing the stage. Overrides publish.skipDuplicates for this invocation; a conflict with the manifest prints one line to standard error and this switch wins.",
        };

        Option<bool> dryRunOption = CliOptions.CreateDryRun();
        Option<bool> verboseOption = CliOptions.CreateVerbose();

        Add(apiKeyOption);
        Add(skipDuplicateOption);
        Add(dryRunOption);
        Add(verboseOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliOutput.ApplyOutputModes(parseResult, verboseOption))
            {
                return 1;
            }

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, missing
            // key, blocked gate, missing package, failed push - is caught here and rendered as
            // the one-line error.
            try
            {
                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                // Resolved exactly once, immediately after the load, and consumed by both
                // the plan path and the run path below: one decision, and the conflict line
                // printed here at this single call site is what caps it at once per
                // invocation.
                SkipDuplicatesDecision skipDecision = NuGetSkipResolver.Resolve(
                    manifest.Publish, parseResult.GetValue(skipDuplicateOption));

                if (skipDecision.ConflictMessage is { } conflictMessage)
                {
                    CliOutput.Error(conflictMessage);
                }

                // The key travels only through the CLI option or the environment. The
                // manifest model has no key-bearing field and strict parse rejects any
                // stray one, so nothing on disk can carry a key or key reference.
                string? apiKey = ResolveApiKey(parseResult.GetValue(apiKeyOption));

                bool dryRun = parseResult.GetValue(dryRunOption);

                if (manifestFile.Directory is null || !Directory.Exists(manifestFile.Directory.FullName))
                {
                    CliOutput.Error(Resources.Exceptions_ManifestFile_NotFound);
                    return 1;
                }

                string directoryPath = manifestFile.Directory.FullName;

                if (dryRun)
                {
                    // A plan pushes nothing, so it needs no credential: a missing key is
                    // reported, never failed, so the exit code stays manifest-validation only.
                    if (apiKey is null)
                    {
                        CliOutput.Note(
                            $"publish: dry run: no API key resolved from --api-key or {ApiKeyEnvironmentVariable}; a real run needs one before it can push.");
                    }

                    await PlanAsync(manifest, directoryPath, skipDecision, cancellationToken).ConfigureAwait(true);
                    return 0;
                }

                if (apiKey is null)
                {
                    // A refused condition is a named report, not an exception.
                    CliOutput.Error(
                        $"publish: no API key. Pass --api-key or set the {ApiKeyEnvironmentVariable} environment variable.");
                    return 1;
                }

                // The gate consult happens before the first push, so a blocked manifest
                // stops with no network call.
                TestGateResult gate = await ConsultGateAsync(
                    manifest, CliOutput.Quiet, CliOutput.Verbose, cancellationToken).ConfigureAwait(true);

                switch (gate.Outcome)
                {
                    case TestGateOutcome.Blocked:
                        CliOutput.Error(
                            $"publish: blocked by the test gate; dotnet test exited {gate.ExitCode} and no packages were pushed.");
                        return 1;

                    case TestGateOutcome.NoSection:
                        CliOutput.Warning(
                            "publish: no test section in the manifest; the test gate passes by default.");
                        break;
                }

                string[] sources = NuGetSourceResolver.Resolve(manifest.Publish.Sources);

                if (sources.Length > 1)
                {
                    CliOutput.Note($"publish: pushing to {sources.Length} sources: {string.Join(", ", sources)}.");
                }

                List<PushEntry> pushes = CollectPushEntries(manifest, directoryPath);

                if (pushes.Count == 0)
                {
                    CliOutput.Note(
                        $"publish: no package kinds declared across {manifest.Targets.Count} target(s); nothing to publish.");
                    return 0;
                }

                int pushedCount = 0;
                int skippedCount = 0;
                List<string> pushedKinds = [];

                foreach (PushEntry push in pushes)
                {
                    foreach (string source in sources)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        bool skipped = await PushAsync(
                            push, source, apiKey, skipDecision, cancellationToken).ConfigureAwait(true);

                        if (skipped)
                        {
                            // A source-declared duplicate is not a push: it joins the
                            // skipped total and never the pushed one.
                            skippedCount++;
                            continue;
                        }

                        pushedCount++;

                        if (!pushedKinds.Contains(push.Kind))
                        {
                            pushedKinds.Add(push.Kind);
                        }
                    }
                }

                // Pushed and skipped are reported as two totals so a re-run states exactly
                // what it re-pushed and what the source judged a duplicate; skips leave the
                // exit at 0.
                string kindsClause = pushedKinds.Count > 0
                    ? $" of kind(s) {string.Join(", ", pushedKinds)}"
                    : string.Empty;

                CliOutput.Note(
                    $"publish: pushed {pushedCount} package(s){kindsClause} across {manifest.Targets.Count} target(s) to {sources.Length} source(s) in manifest target order; skipped {skippedCount} package(s).");

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
    /// Resolves the API key through the API-key channels: the <c>--api-key</c> option wins, then
    /// the <c>NUGET_API_KEY</c> environment variable; both blank yields null and the command
    /// refuses before any work. Nothing on disk is ever consulted.
    /// </summary>
    /// <param name="optionValue">The <c>--api-key</c> option's value, or null when absent.</param>
    /// <returns>The resolved key, or null when no channel supplied one.</returns>
    private static string? ResolveApiKey(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    /// <summary>
    /// Prints the whole publish plan under <c>--dry-run</c>: the would-be gate consult, the
    /// would-be pushes as shared planned records the runner prints without spawning, the
    /// resolved skip mode each source would push under, and the closing plan-not-certification
    /// note. Nothing here runs tests, writes <c>TestResults/</c>, reads the package tree, or
    /// pushes - a plan must fail on manifest validation alone, so it names each kind's package
    /// directory rather than the concrete packages a real run enumerates.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    /// <param name="manifestDirectory">The manifest's directory, for resolving relative project paths.</param>
    /// <param name="skipDecision">The once-resolved skip decision this plan mirrors.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    private async Task PlanAsync(
        Manifest manifest,
        string manifestDirectory,
        SkipDuplicatesDecision skipDecision,
        CancellationToken cancellationToken)
    {
        PrintGateConsult(manifest);

        string[] sources = NuGetSourceResolver.Resolve(manifest.Publish.Sources);

        if (sources.Length > 1)
        {
            CliOutput.Note($"publish: dry run: would push to {sources.Length} sources: {string.Join(", ", sources)}.");
        }

        // The plan states the resolved mode per source once for the whole plan, not once per
        // target - the mode is source-specific and target-independent - and only when some
        // target declares a package kind: with nothing to push, the mode a push would run
        // under is noise beside the "nothing to publish" note below.
        bool declaresPackageKind = manifest.Targets.Any(static target =>
            target.Kinds.Contains(ArtifactKinds.LibPackage, StringComparer.Ordinal) ||
            target.Kinds.Contains(ArtifactKinds.ToolPackage, StringComparer.Ordinal));

        if (declaresPackageKind)
        {
            foreach (string source in sources)
            {
                CliOutput.Note(skipDecision.SkipEnabled
                    ? $"publish: dry run: source '{source}': resolved skip mode: duplicates skipped; the push would pass --skip-duplicate."
                    : $"publish: dry run: source '{source}': resolved skip mode: duplicates fail the stage; no --skip-duplicate would be passed.");
            }
        }

        int plannedCount = 0;

        foreach (ManifestTarget target in manifest.Targets)
        {
            plannedCount += await PlanTargetPushesAsync(
                target, manifestDirectory, sources, skipDecision, cancellationToken).ConfigureAwait(true);
        }

        if (plannedCount == 0)
        {
            CliOutput.Note(
                $"publish: dry run: no package kinds declared across {manifest.Targets.Count} target(s); nothing to publish.");
        }

        CliOutput.Note(
            "publish: dry run: pushed nothing; a zero-exit dry run is a plan, not a certification - run publish for real before pushing.");
    }

    /// <summary>
    /// Prints one target's would-be pushes - one planned record per declared package kind per
    /// resolved source, in the same manifest order a real run pushes in. The resolved skip
    /// mode each source would push under prints once per source in <see cref="PlanAsync"/>,
    /// before any target's pushes, so a plan rehearses the same decision a real run consumes.
    /// </summary>
    /// <param name="target">The manifest target whose kinds are planned.</param>
    /// <param name="manifestDirectory">The manifest's directory, for resolving relative project paths.</param>
    /// <param name="sources">The resolved push sources.</param>
    /// <param name="skipDecision">The once-resolved skip decision this plan mirrors.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>How many planned pushes printed for this target.</returns>
    private async Task<int> PlanTargetPushesAsync(
        ManifestTarget target,
        string manifestDirectory,
        string[] sources,
        SkipDuplicatesDecision skipDecision,
        CancellationToken cancellationToken)
    {
        FileInfo projectFile = _manifestLoader.ResolveProjectFile(target.Project, manifestDirectory);
        string projectName = Path.GetFileNameWithoutExtension(projectFile.Name);
        int plannedCount = 0;
        string skipFlag = skipDecision.SkipEnabled ? " --skip-duplicate" : string.Empty;

        foreach (string kind in target.Kinds)
        {
            // The executables kind is the build stage's product, never a push.
            if (string.Equals(kind, ArtifactKinds.Executables, StringComparison.Ordinal))
            {
                continue;
            }

            string kindFolder = kind switch
            {
                ArtifactKinds.LibPackage => "lib",
                ArtifactKinds.ToolPackage => "tool",
                _ => throw new PublishStageException(
                    $"publish: unknown artifact kind '{kind}'; the publish stage pushes '{ArtifactKinds.LibPackage}' and '{ArtifactKinds.ToolPackage}'."),
            };

            string packagesPath = Path.Combine(
                Environment.CurrentDirectory, "release", "packages", kindFolder, projectName, "*.nupkg");

            foreach (string source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The printed line is the real push's shape with the key redacted, so the
                // plan and the run read as one command set.
                PlannedCommand command = new()
                {
                    CommandLine =
                        $"publish: dotnet nuget push {packagesPath} --source {source} --api-key ****{skipFlag} (kind '{kind}' for target '{projectName}').",
                    ArtifactPaths = [packagesPath],
                };

                await _plannedCommandRunner.RunAsync(
                    command, StageRunMode.DryRun, CliOutput.Note, cancellationToken).ConfigureAwait(true);
                plannedCount++;
            }
        }

        return plannedCount;
    }

    /// <summary>
    /// Prints the would-be gate consult for the dry-run report: an absent test section warns
    /// exactly as a real run does, and a declared section names the test projects the gate
    /// would run instead of running them - no tests execute and no <c>TestResults/</c> is
    /// written.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    private static void PrintGateConsult(Manifest manifest)
    {
        if (manifest.Test is null)
        {
            CliOutput.Warning("publish: no test section in the manifest; the test gate passes by default.");
            return;
        }

        string projects = manifest.Test.Projects.Length == 0
            ? "every test project in the current directory (the test section names none)"
            : string.Join(", ", manifest.Test.Projects);

        CliOutput.Note(
            $"publish: dry run: tests would run for: {projects}; the gate executed nothing and wrote no TestResults/.");
    }

    /// <summary>
    /// Consults the test gate before the first push. An outcome recorded in this process
    /// - the chained run records the test stage's result through <see cref="StageContext"/>
    /// - is consumed as-is. Otherwise, an absent test section lands on the warning path,
    /// and a declared section is consulted by running the test stage itself: the gate consumes
    /// that invocation's exit code, so a standalone publish cannot bypass it.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    /// <param name="quiet">Whether stage notes are suppressed, forwarded to the test consult.</param>
    /// <param name="verbose">Whether the stage runs under <c>--verbose</c>, forwarded to the test consult.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The gate outcome.</returns>
    private async Task<TestGateResult> ConsultGateAsync(
        Manifest manifest,
        bool quiet,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (StageContext.TestGate is { } recorded)
        {
            return recorded;
        }

        if (manifest.Test is null)
        {
            // Warning path: an absent test section passes by default; nothing runs.
            return TestGateResult.FromStage(exitCode: 0, hasTestSection: false);
        }

        // Standalone publish consults the gate by running the test stage: a declared
        // section must exit 0 before the first push.
        CliOutput.Note("publish: consulting the test gate; running the test stage.");

        int exitCode = await InvokeTestStageAsync(quiet, verbose, cancellationToken).ConfigureAwait(true);
        TestGateResult gate = TestGateResult.FromStage(exitCode, hasTestSection: true);

        StageContext.TestGate = gate;

        return gate;
    }

    /// <summary>
    /// Runs the test stage command in-process so the gate consult consumes exactly the stage's
    /// exit code, replaying its console output through the conventions layer. The stage sees the
    /// same output-mode flags a user would pass it, so the consult stays as quiet or as
    /// verbose as the publish invocation it serves.
    /// </summary>
    /// <param name="quiet">Whether <c>--quiet</c> was set on this invocation; forwarded so the consult stays as quiet.</param>
    /// <param name="verbose">Whether <c>--verbose</c> was set on this invocation; forwarded for the same reason.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The test stage's exit code.</returns>
    private async Task<int> InvokeTestStageAsync(
        bool quiet,
        bool verbose,
        CancellationToken cancellationToken)
    {
        RootCommand temporaryRoot = new("ReleaseGen stage invocation root.")
        {
            new TestCommand(_manifestLoader, _processInvoker),
            CliOptions.Quiet,
        };

        List<string> argv = ["test"];

        if (quiet)
        {
            argv.Add("--quiet");
        }

        if (verbose)
        {
            argv.Add("--verbose");
        }

        ParseResult result = temporaryRoot.Parse(argv);

        return await result.InvokeAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Collects the full push list before the first push: every declared package kind of
    /// every target, in manifest target order, each kind's packages enumerated from the package
    /// stage's local output. A kind with no package output fails the stage naming the directory
    /// with no partial upload; the executables kind is skipped, never pushed.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    /// <param name="manifestDirectory">The manifest's directory, for resolving relative project paths.</param>
    /// <returns>The packages to push, in manifest target and declared-kind order.</returns>
    /// <exception cref="PublishStageException">A declared package kind has no package output to push.</exception>
    private List<PushEntry> CollectPushEntries(Manifest manifest, string manifestDirectory)
    {
        DirectoryInfo packagesRoot = new(Path.Combine(Environment.CurrentDirectory, "release", "packages"));
        List<PushEntry> entries = [];

        foreach (ManifestTarget target in manifest.Targets)
        {
            FileInfo projectFile = _manifestLoader.ResolveProjectFile(target.Project, manifestDirectory);
            string projectName = Path.GetFileNameWithoutExtension(projectFile.Name);

            foreach (string kind in target.Kinds)
            {
                // The executables kind is the build stage's product, not a package.
                if (string.Equals(kind, ArtifactKinds.Executables, StringComparison.Ordinal))
                {
                    continue;
                }

                string kindFolder = kind switch
                {
                    ArtifactKinds.LibPackage => "lib",
                    ArtifactKinds.ToolPackage => "tool",
                    _ => throw new PublishStageException(
                        $"publish: unknown artifact kind '{kind}'; the publish stage pushes '{ArtifactKinds.LibPackage}' and '{ArtifactKinds.ToolPackage}'."),
                };

                DirectoryInfo kindDirectory = new(Path.Combine(packagesRoot.FullName, kindFolder, projectName));

                // Fresh OS-level checks: DirectoryInfo/FileInfo cache their first probe.
                if (!Directory.Exists(kindDirectory.FullName))
                {
                    throw new PublishStageException(
                        $"publish: no package output at '{kindDirectory.FullName}' for target '{projectName}' with kind '{kind}'; run the package stage before publish.");
                }

                FileInfo[] packages =
                [
                    .. Directory.EnumerateFiles(kindDirectory.FullName, "*.nupkg")
                        .Where(static file => !file.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(static file => file, StringComparer.Ordinal)
                        .Select(static file => new FileInfo(file)),
                ];

                if (packages.Length == 0)
                {
                    throw new PublishStageException(
                        $"publish: no .nupkg files under '{kindDirectory.FullName}' for target '{projectName}' with kind '{kind}'; run the package stage before publish.");
                }

                foreach (FileInfo package in packages)
                {
                    entries.Add(new PushEntry(package, kind, projectName));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Runs one <c>dotnet nuget push</c> and hands it to the runner: the streamed-args note
    /// with the key redacted is the record's command line, so both printed modes show the same
    /// line without leaking the credential, and only a real run spawns the push. When the
    /// resolved decision enables skipping, <c>--skip-duplicate</c> joins the argument list and
    /// the printed line - the source alone judges duplicates, so nothing queries it first.
    /// A nonzero exit fails fast with
    /// <see cref="PublishStageException"/> naming the package, the source, and the exit code;
    /// failure detail stays textual. An exit-zero push whose merged streams carry
    /// <see cref="SkipDuplicateMarker"/> counts as skipped instead of pushed: the marker line
    /// re-emits as a note rather than a warning, and a stage note names the package and the
    /// source.
    /// </summary>
    /// <param name="push">The package to push.</param>
    /// <param name="source">The resolved push source.</param>
    /// <param name="apiKey">The API key from the API-key channels; redacted from the console.</param>
    /// <param name="skipDecision">The once-resolved skip decision; its <c>SkipEnabled</c> appends the switch.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>True when the source declared this push a duplicate and it counted as skipped; false when it counted as pushed.</returns>
    private async Task<bool> PushAsync(
        PushEntry push,
        string source,
        string apiKey,
        SkipDuplicatesDecision skipDecision,
        CancellationToken cancellationToken)
    {
        PlannedCommand command = BuildPushCommand(push, source, apiKey, skipDecision);

        BufferedProcessResult? result = await _plannedCommandRunner.RunAsync(
            command, StageRunMode.Execute, CliOutput.Note, cancellationToken, CliOutput.Cause).ConfigureAwait(true);

        if (result is null)
        {
            // Unreachable here - the dry-run branch returned before any push was planned -
            // but the runner's contract still decides per mode: no result means nothing ran.
            return false;
        }

        bool skipped = await ReplayPushOutputAsync(result, push, source, cancellationToken).ConfigureAwait(true);

        if (result.ExitCode != 0)
        {
            // Unchanged fail-fast: only an exit-zero duplicate is a skip; a real nonzero
            // push exit still aborts the stage.
            throw new PublishStageException(
                $"publish: dotnet nuget push failed for package '{push.PackageFile.Name}' (kind '{push.Kind}', target '{push.TargetProject}') to source '{source}' (exit code {result.ExitCode}).");
        }

        if (skipped)
        {
            CliOutput.Note(
                $"publish: skipped package '{push.PackageFile.Name}' to source '{source}'; the source declared it a duplicate.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the executed record for one <c>dotnet nuget push</c>: the argument list the
    /// process runs with, and the same command line with the key redacted for the console.
    /// <c>--skip-duplicate</c> joins both exactly when the decision enables it - the switch
    /// rides the existing push path with no pre-check and no source query of any kind.
    /// </summary>
    /// <param name="push">The package to push.</param>
    /// <param name="source">The resolved push source.</param>
    /// <param name="apiKey">The API key from the API-key channels; carried, never printed.</param>
    /// <param name="skipDecision">The once-resolved skip decision; its <c>SkipEnabled</c> appends the switch.</param>
    /// <returns>The planned record the runner executes.</returns>
    private static PlannedCommand BuildPushCommand(
        PushEntry push,
        string source,
        string apiKey,
        SkipDuplicatesDecision skipDecision)
    {
        // No --non-interactive: the API key is always supplied through the API-key channels, so
        // no auth prompt can occur, and the flag makes NuGet's local-folder symbols probe fail
        // the push with 'File does not exist' after the package landed (the local-source
        // integration destination would never exit 0 with it).
        List<string> argumentList =
        [
            "nuget",
            "push",
            push.PackageFile.FullName,
            "--source",
            source,
            "--api-key",
            apiKey,
        ];

        string skipFlag = string.Empty;

        if (skipDecision.SkipEnabled)
        {
            argumentList.Add("--skip-duplicate");
            skipFlag = " --skip-duplicate";
        }

        // The printed command line redacts the key; the executed argument list carries it.
        return new PlannedCommand
        {
            CommandLine =
                $"publish: dotnet nuget push {push.PackageFile.FullName} --source {source} --api-key ****{skipFlag} (kind '{push.Kind}' for target '{push.TargetProject}').",
            TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
            ArgumentList = argumentList,
            WorkingDirectory = Environment.CurrentDirectory,
            ArtifactPaths = [push.PackageFile.FullName],
            Diagnosis = () =>
                $"publish: this push uploads '{push.PackageFile.Name}' (kind '{push.Kind}', target '{push.TargetProject}') to '{source}'; the first nonzero exit aborts the stage.",
        };
    }

    /// <summary>
    /// Replays one push's captured output to the console and reports whether the source
    /// declared a duplicate: both streams are scanned for <see cref="SkipDuplicateMarker"/>
    /// with a strict ordinal match, so counting never depends on which stream the notice
    /// arrived on, and an exit-zero push without the marker still counts as pushed. A matched
    /// stderr line leaves the warning path and re-emits as a note, so a skip never renders as
    /// a warning.
    /// </summary>
    /// <param name="result">The captured push result.</param>
    /// <param name="push">The package that was pushed, for the truncation wording.</param>
    /// <param name="source">The resolved push source, for the truncation wording.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>True when either stream carried the skip marker.</returns>
    private static async Task<bool> ReplayPushOutputAsync(
        BufferedProcessResult result,
        PushEntry push,
        string source,
        CancellationToken cancellationToken)
    {
        if (result.WasTruncated)
        {
            CliOutput.Warning(
                $"publish: push output for package '{push.PackageFile.Name}' to source '{source}' was truncated; some lines did not reach the console.");
        }

        bool skipped = ContainsSkipMarker(result.StandardOutput) || ContainsSkipMarker(result.StandardError);

        // stdout lines are stage notes already; a stderr marker line leaves the warning path
        // and re-emits as a note, so a skip never renders as a warning.
        await ReplayLines(result.StandardOutput, static line => CliOutput.Note(line), cancellationToken).ConfigureAwait(true);
        await ReplayLines(
            result.StandardError,
            static line =>
            {
                if (ContainsSkipMarker(line))
                {
                    CliOutput.Note(line);
                }
                else
                {
                    CliOutput.Warning(line);
                }
            },
            cancellationToken).ConfigureAwait(true);

        return skipped;
    }

    /// <summary>
    /// Whether the captured output carries the fixture-pinned skip marker. The match is strict
    /// and ordinal - the exact marker text and nothing looser - so wording drift in
    /// <c>dotnet nuget push</c> undercounts skipped pushes rather than inventing them, and no
    /// source query ever substitutes for the marker.
    /// </summary>
    /// <param name="text">The captured stdout or stderr text, or a single line of it; null reads as no match.</param>
    /// <returns>True when <paramref name="text"/> carries <see cref="SkipDuplicateMarker"/>.</returns>
    private static bool ContainsSkipMarker(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(SkipDuplicateMarker, StringComparison.Ordinal);

    /// <summary>
    /// Replays captured process output to the sink one line at a time, dropping only empty
    /// lines. No content-based filtering, mirroring the other stages' replay.
    /// </summary>
    /// <param name="text">The captured stdout or stderr text.</param>
    /// <param name="sink">The line sink; null swallows the lines.</param>
    /// <param name="cancellationToken"></param>
    private static async Task ReplayLines(string? text, Action<string>? sink, CancellationToken cancellationToken)
    {
        if (sink is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        using StringReader reader = new(text);

        while (await reader.ReadLineAsync(cancellationToken)
                   .ConfigureAwait(true) is { } line)
        {
            if (line.Length > 0)
            {
                sink(line);
            }
        }
    }

    /// <summary>
    /// One package the publish stage pushes: the package file, its declared kind, and the
    /// target that declared it - the textual failure detail names all three.
    /// </summary>
    /// <param name="PackageFile">The .nupkg file produced by the package stage.</param>
    /// <param name="Kind">The target's declared package kind.</param>
    /// <param name="TargetProject">The target's project name, for console wording.</param>
    private sealed record PushEntry(FileInfo PackageFile, string Kind, string TargetProject);
}