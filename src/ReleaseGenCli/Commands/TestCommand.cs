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
/// default. The stage records its gate outcome for the publish stage to consume;
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

    public TestCommand(IManifestLoader manifestLoader, IProcessInvoker processInvoker) : base("test", "Runs dotnet test on the manifest's test projects.")
    {
        _manifestLoader = manifestLoader;
        SetAction(async (parseResult, cancellationToken) =>
        {
            CliOutput.ApplyQuiet(parseResult);

            // System.CommandLine swallows exceptions thrown by async action handlers and prints
            // them with a raw stack trace, so every failure surface - manifest error, unresolved
            // project, failed test run - is caught here and rendered as the one-line error.
            try
            {
                FileInfo manifestFile = manifestLoader.FindManifestFile(new DirectoryInfo(Environment.CurrentDirectory));
                Manifest manifest = manifestLoader.LoadFromFile(manifestFile);

                if (manifest.Test is null)
                {
                    // Warning path: an absent test section warns, never fails.
                    CliOutput.Warning("test: no test section in the manifest; the test gate passes by default.");
                    return 0;
                }
                
                int exitCode = await RunTestsAsync(
                    processInvoker,
                    manifest.Test.Projects,
                    manifestFile.Directory!.FullName,
                    cancellationToken).ConfigureAwait(true);

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
    /// <param name="processInvoker">The CliInvoke process invoker.</param>
    /// <param name="projects">The test section's named test projects; empty means the cwd fallback.</param>
    /// <param name="manifestDirectory">The manifest's directory, the working directory for named-project runs.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The first nonzero exit code, or 0 when every invocation passed.</returns>
    private async Task<int> RunTestsAsync(
        IProcessInvoker processInvoker,
        string[] projects,
        string manifestDirectory,
        CancellationToken cancellationToken)
    {
        if (projects.Length == 0)
        {
            // Cwd fallback: the section names no projects, so run from the current directory.
            return await RunDotnetTestAsync(
                processInvoker, ["test"], Environment.CurrentDirectory, project: null, cancellationToken).ConfigureAwait(true);
        }

        foreach (string project in projects)
        {
            FileInfo projectFile = _manifestLoader.ResolveProjectFile(project, manifestDirectory);

            int exitCode = await RunDotnetTestAsync(
                    processInvoker, ["test", projectFile.FullName], manifestDirectory, project, cancellationToken)
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
    /// Runs one <c>dotnet test</c> invocation through CliInvoke, replaying its captured output
    /// through the conventions layer and emitting the one-line error - naming the
    /// project, or the current directory for the cwd fallback - on a nonzero exit.
    /// </summary>
    /// <param name="processInvoker">The CliInvoke process invoker.</param>
    /// <param name="argumentList">The <c>dotnet</c> argument list for this invocation.</param>
    /// <param name="workingDirectory">The process working directory.</param>
    /// <param name="project">The named project for the error wording, or null for the cwd fallback.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <returns>The invocation's exit code.</returns>
    private static async Task<int> RunDotnetTestAsync(
        IProcessInvoker processInvoker,
        List<string> argumentList,
        string workingDirectory,
        string? project,
        CancellationToken cancellationToken)
    {
        // Streamed args note: makes the resolved invocation visible on the console, like the
        // build stage's 'build: dotnet ...' line (the evidence surface).
        CliOutput.Note($"test: dotnet {string.Join(' ', argumentList)}");

        ProcessConfiguration configuration =
            new(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet", argumentList)
            {
                WorkingDirectoryPath = workingDirectory,
            };

        BufferedProcessResult result = await processInvoker.ExecuteBufferedAsync(
            configuration,
            ProcessExitConfiguration.Default,
            cancellationToken).ConfigureAwait(true);

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
