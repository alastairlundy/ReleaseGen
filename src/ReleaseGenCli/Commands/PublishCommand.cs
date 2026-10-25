using ReleaseGenCli.Exceptions;

namespace ReleaseGenCli.Commands;

using System.CommandLine;
using ReleaseGenLib.Models;

/// <summary>
/// The publish stage: pushes each target's declared package kinds with
/// <c>dotnet nuget push</c> through CliInvoke, in manifest target order, to the
/// resolved sources. The API key arrives only through the <c>--api-key</c> option or the
/// <see cref="ApiKeyEnvironmentVariable"/> environment variable - never the manifest, whose
/// strict parse rejects any key-bearing field outright. Before the first push the
/// stage consults the test gate: a blocked outcome stops the stage with an error before
/// any network call, and an absent test section proceeds with a warning. Failure detail is
/// textual, not structured, in v1: the first failing push aborts the stage with a
/// one-line error naming the package, the source, and the exit code.
/// </summary>
/// <remarks>
/// Packages are the package stage's local product: each declared package kind maps to
/// <c>release/packages/lib|tool/&lt;project-name&gt;/*.nupkg</c> under the invocation
/// directory, and the full push list is collected before the first push so a missing package
/// fails the stage with no partial upload. The executables kind is the build stage's product,
/// not a NuGet package, and is skipped here, never pushed. Push output reaches the
/// console through the conventions layer - stdout lines become stage notes and stderr
/// lines become warnings - and the streamed-args note redacts the API key so the key never
/// reaches the console.
/// </remarks>
public sealed class PublishCommand : Command
{
    private readonly IManifestLoader _manifestLoader;
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    /// The environment-variable channel for the API key, consulted when the
    /// <c>--api-key</c> option carries no value.
    /// </summary>
    private const string ApiKeyEnvironmentVariable = "NUGET_API_KEY";

    public PublishCommand(IManifestLoader manifestLoader, IProcessInvoker processInvoker) : base("publish",
        "Publishes each target's declared package kinds to the configured NuGet sources.")
    {
        _manifestLoader = manifestLoader;
        _processInvoker = processInvoker;
        Option<string?> apiKeyOption = new("--api-key")
        {
            Description =
                $"The NuGet API key. Falls back to the {ApiKeyEnvironmentVariable} environment variable. Never read from the manifest.",
        };

        Add(apiKeyOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, missing
            // key, blocked gate, missing package, failed push - is caught here and rendered as
            // the one-line error.
            try
            {
                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                // The key travels only through the CLI option or the environment. The
                // manifest model has no key-bearing field and strict parse rejects any
                // stray one, so nothing on disk can carry a key or key reference.
                string? apiKey = ResolveApiKey(parseResult.GetValue(apiKeyOption));

                if (apiKey is null)
                {
                    // A refused condition is a named report, not an exception.
                    CliOutput.Error(
                        $"publish: no API key. Pass --api-key or set the {ApiKeyEnvironmentVariable} environment variable.");
                    return 1;
                }
                
                if (manifestFile.Directory is null || !Directory.Exists(manifestFile.Directory.FullName))
                {
                    CliOutput.Error(Resources.Exceptions_ManifestFile_NotFound);
                    return 1;
                }
                
                string directoryPath = manifestFile.Directory.FullName;

                // The gate consult happens before the first push, so a blocked manifest
                // stops with no network call.
                TestGateResult gate = await ConsultGateAsync(manifest, CliOutput.Quiet, cancellationToken).ConfigureAwait(true);

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
                List<string> pushedKinds = [];

                foreach (PushEntry push in pushes)
                {
                    foreach (string source in sources)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await PushAsync(processInvoker, push, source, apiKey, cancellationToken).ConfigureAwait(true);
                        pushedCount++;

                        if (!pushedKinds.Contains(push.Kind))
                        {
                            pushedKinds.Add(push.Kind);
                        }
                    }
                }

                CliOutput.Note(
                    $"publish: pushed {pushedCount} package(s) of kind(s) {string.Join(", ", pushedKinds)} across {manifest.Targets.Count} target(s) to {sources.Length} source(s) in manifest target order.");

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
    /// Consults the test gate before the first push. An outcome recorded in this process
    /// - the chained run records the test stage's result through <see cref="StageContext"/>
    /// - is consumed as-is. Otherwise, an absent test section lands on the warning path,
    /// and a declared section is consulted by running the test stage itself: the gate consumes
    /// that invocation's exit code, so a standalone publish cannot bypass it.
    /// </summary>
    /// <param name="manifest">The loaded manifest.</param>
    /// <param name="quiet">Whether stage notes are suppressed, forwarded to the test consult.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The gate outcome.</returns>
    private async Task<TestGateResult> ConsultGateAsync(
        Manifest manifest,
        bool quiet,
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

        int exitCode = await InvokeTestStageAsync(quiet, cancellationToken).ConfigureAwait(true);
        TestGateResult gate = TestGateResult.FromStage(exitCode, hasTestSection: true);

        StageContext.TestGate = gate;

        return gate;
    }

    /// <summary>
    /// Runs the test stage command in-process so the gate consult consumes exactly the stage's
    /// exit code, replaying its console output through the conventions layer.
    /// </summary>
    /// <param name="quiet">Whether <c>--quiet</c> was set on this invocation; forwarded so the consult stays as quiet.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The test stage's exit code.</returns>
    private async Task<int> InvokeTestStageAsync(
        bool quiet,
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
    /// Runs one <c>dotnet nuget push</c> through CliInvoke for one package and one
    /// source. A nonzero exit fails fast with <see cref="PublishStageException"/> naming the
    /// package, the source, and the exit code; failure detail stays textual.
    /// </summary>
    /// <param name="processInvoker">The CliInvoke process invoker.</param>
    /// <param name="push">The package to push.</param>
    /// <param name="source">The resolved push source.</param>
    /// <param name="apiKey">The API key from the API-key channels; redacted from the console.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    private static async Task PushAsync(
        IProcessInvoker processInvoker,
        PushEntry push,
        string source,
        string apiKey,
        CancellationToken cancellationToken)
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

        // Streamed args note with the key redacted: the resolved invocation stays visible on
        // the console (the evidence surface) without leaking the credential.
        CliOutput.Note(
            $"publish: dotnet nuget push {push.PackageFile.FullName} --source {source} --api-key **** (kind '{push.Kind}' for target '{push.TargetProject}').");

        ProcessConfiguration configuration =
            new(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", argumentList)
            {
                WorkingDirectoryPath = Environment.CurrentDirectory,
            };

        BufferedProcessResult result = await processInvoker.ExecuteBufferedAsync(
            configuration,
            ProcessExitConfiguration.Default,
            cancellationToken).ConfigureAwait(true);

        if (result.WasTruncated)
        {
            CliOutput.Warning(
                $"publish: push output for package '{push.PackageFile.Name}' to source '{source}' was truncated; some lines did not reach the console.");
        }

        await ReplayLines(result.StandardOutput, static line => CliOutput.Note(line), cancellationToken).ConfigureAwait(true);
        await ReplayLines(result.StandardError, static line => CliOutput.Warning(line), cancellationToken).ConfigureAwait(true);

        if (result.ExitCode != 0)
        {
            // Textual failure posture in the error style: names the package, the
            // source, and the exit code.
            throw new PublishStageException(
                $"publish: dotnet nuget push failed for package '{push.PackageFile.Name}' (kind '{push.Kind}', target '{push.TargetProject}') to source '{source}' (exit code {result.ExitCode}).");
        }
    }

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