/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace ReleaseGen.Cli.Commands;

/// <summary>
/// The test stage: <c>dotnet test</c> through CliInvoke on the projects the manifest's
/// test section names, or once from the current directory when the section lists none.
/// A declared section that exits nonzero fails the stage with a one-line error naming the
/// project and the exit code and returns 1, so a chained run aborts before publish;
/// a manifest with no test section warns and returns 0 - the test gate passes by
/// default. Each test invocation is a shared planned record: the run mode decides at the
/// runner boundary whether the stage executes it or prints it without running anything, and
/// <c>--dry-run</c> is that flag - it prints the invocation plan, runs no tests, writes no
/// <c>TestResults/</c>, and exits on manifest validation alone.
/// The stage records its gate outcome for the publish stage to consume;
/// there is no runner-override key or gate-forcing flag in v1.
/// </summary>
/// <remarks>
/// Output reaches the console only through the conventions layer: stdout lines become
/// stage notes except MSBuild diagnostic lines (<c>: warning </c>/<c>: error </c>), which
/// become warnings alongside all stderr lines so they stay visible under <c>--quiet</c>.
/// No test line is dropped.
/// </remarks>
public sealed class TestCommand : Command
{
    private readonly IManifestLoader _manifestLoader;
    private readonly PlannedCommandRunner _plannedCommandRunner;

    public TestCommand(IManifestLoader manifestLoader, IProcessInvoker processInvoker) : base("test", "Runs dotnet test on the manifest's test projects.")
    {
        _manifestLoader = manifestLoader;
        _plannedCommandRunner = new PlannedCommandRunner(processInvoker);

        Option<bool> dryRunOption = CliOptions.CreateDryRun();
        Add(dryRunOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, unresolved
            // project, failed test run - is caught here and rendered as the one-line error.
            try
            {
                // One decision point for the runner: the flag.
                StageRunMode runMode = parseResult.GetValue(dryRunOption)
                    ? StageRunMode.DryRun
                    : StageRunMode.Execute;

                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                if (manifest.Test is null)
                {
                    // Warning path: an absent test section warns, never fails.
                    CliOutput.Warning("test: no test section in the manifest; the test gate passes by default.");
                    return 0;
                }
                
                int exitCode = await RunTestsAsync(
                    manifest.Test.Projects,
                    manifestFile.Directory!.FullName,
                    runMode,
                    cancellationToken).ConfigureAwait(true);

                if (runMode == StageRunMode.DryRun)
                {
                    // The plan above is the whole report: nothing executed, so nothing can
                    // fail and the exit code reflects manifest validation only.
                    CliOutput.Note("test: dry run: no tests ran and no TestResults/ was written.");
                    return 0;
                }

                if (exitCode != 0)
                {
                    // Fail-fast: the error already names the project (or the cwd fallback)
                    // and the exit code; a chained run stops here, before publish.
                    return 1;
                }

                // Gate input for the publish stage: a declared section that exited 0
                // records Passed. The publishing stage consumes this in a later ticket; for now
                // the local is the whole hand-off surface.
                TestGateResult gateResult = TestGateResult.FromStage(exitCode, hasTestSection: true);

                return gateResult.Outcome == TestGateOutcome.Passed ? 0 : 1;
            }
            catch (Exception ex)
            {
                CliOutput.Error(ex.Message);
                return 1;
            }
        });
    }

    /// <summary>
    /// Runs <c>dotnet test</c> for each project the test section names, or once from the current
    /// directory when none are named, fail-fast on the first nonzero exit: later
    /// projects are not run after a failure.
    /// </summary>
    /// <param name="projects">The test section's named test projects; empty means the cwd fallback.</param>
    /// <param name="manifestDirectory">The manifest's directory, the working directory for named-project runs.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The first nonzero exit code, or 0 when every invocation passed.</returns>
    private async Task<int> RunTestsAsync(
        string[] projects,
        string manifestDirectory,
        StageRunMode runMode,
        CancellationToken cancellationToken)
    {
        if (projects.Length == 0)
        {
            // Cwd fallback: the section names no projects, so run from the current directory.
            return await RunDotnetTestAsync(
                ["test"], Environment.CurrentDirectory, project: null, runMode, cancellationToken).ConfigureAwait(true);
        }

        foreach (string project in projects)
        {
            FileInfo projectFile = _manifestLoader.ResolveProjectFile(project, manifestDirectory);

            int exitCode = await RunDotnetTestAsync(
                    ["test", projectFile.FullName], manifestDirectory, project, runMode, cancellationToken)
                .ConfigureAwait(true);

            if (exitCode != 0)
            {
                // Fail-fast - do not continue to later projects after a failure.
                return exitCode;
            }
        }

        return 0;
    }

    /// <summary>
    /// Plans one <c>dotnet test</c> invocation as a shared record and hands it to the runner:
    /// the runner prints the streamed-args line (the evidence surface) in both modes, executes
    /// the process only in a real run, and prints the plan without running anything under the
    /// dry-run report, where nothing ran so nothing can fail.
    /// </summary>
    /// <param name="argumentList">The <c>dotnet</c> argument list for this invocation.</param>
    /// <param name="workingDirectory">The process working directory.</param>
    /// <param name="project">The named project for the error wording, or null for the cwd fallback.</param>
    /// <param name="runMode">The runner's decision for this invocation: execute the test run, or print the plan.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The invocation's exit code, or 0 when the dry-run report printed the plan.</returns>
    private async Task<int> RunDotnetTestAsync(
        List<string> argumentList,
        string workingDirectory,
        string? project,
        StageRunMode runMode,
        CancellationToken cancellationToken)
    {
        PlannedCommand command = new()
        {
            CommandLine = $"test: dotnet {string.Join(' ', argumentList)}",
            TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
            ArgumentList = argumentList,
            WorkingDirectory = workingDirectory,
        };

        BufferedProcessResult? result = await _plannedCommandRunner.RunAsync(
            command, runMode, CliOutput.Note, cancellationToken).ConfigureAwait(true);

        if (result is null)
        {
            // Dry run: the plan printed, nothing executed, so the stage reports clean; the
            // command's closing note records that no TestResults/ was written either.
            return 0;
        }

        if (result.WasTruncated)
        {
            CliOutput.Warning(project is null
                ? "test: dotnet test output from the current directory was truncated; some lines did not reach the console."
                : $"test: dotnet test output for project '{project}' was truncated; some lines did not reach the console.");
        }

        ReplayLines(result.StandardOutput, RouteStdoutLine);
        ReplayLines(result.StandardError, static line => CliOutput.Warning(line));

        if (result.ExitCode != 0)
        {
            // The error style: names the project (or the cwd fallback) and the exit code.
            CliOutput.Error(project is null
                ? $"test: dotnet test from the current directory failed (exit code {result.ExitCode})."
                : $"test: dotnet test failed for project '{project}' (exit code {result.ExitCode}).");
        }

        return result.ExitCode;
    }

    /// <summary>
    /// Replays captured process output to the sink one line at a time, dropping only empty
    /// lines. No content-based filtering: test output passes through intact.
    /// </summary>
    /// <param name="text">The captured stdout or stderr text.</param>
    /// <param name="sink">The line sink; null swallows the lines.</param>
    private static void ReplayLines(string? text, Action<string>? sink)
    {
        if (sink is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        using StringReader reader = new(text);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0)
            {
                sink(line);
            }
        }
    }

    /// <summary>
    /// Routes one captured test stdout line through the conventions layer: MSBuild
    /// diagnostic lines - the shape SDK test-run warnings arrive in - become warnings so
    /// they survive <c>--quiet</c>; everything else becomes a stage note.
    /// </summary>
    /// <param name="line">The stdout line exactly as the test run emitted it.</param>
    private static void RouteStdoutLine(string line)
    {
        string trimmed = line.TrimStart();

        bool diagnostic =
            line.Contains(": warning ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains(": error ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("warning ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("error ", StringComparison.OrdinalIgnoreCase);

        if (diagnostic)
        {
            CliOutput.Warning(line);
        }
        else
        {
            CliOutput.Note(line);
        }
    }
}
