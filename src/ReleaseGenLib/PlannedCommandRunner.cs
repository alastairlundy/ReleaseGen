namespace ReleaseGenLib;

using CliInvoke.Core;
/// <summary>
/// The runner boundary: executes one <see cref="PlannedCommand"/> or prints it, decided solely
/// by the <see cref="StageRunMode"/> argument, so one plan serves every mode. Printing never
/// runs the record's process or local side effect, never evaluates its runtime guard, and never
/// surfaces its diagnosis text - nothing has been verified. Executing prints the same command
/// line first (the evidence surface), runs the record's process or local side effect, and may
/// surface the diagnosis through a sink.
/// </summary>
public sealed class PlannedCommandRunner
{
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    /// The runner boundary: executes one <see cref="PlannedCommand"/> or prints it, decided solely
    /// by the <see cref="StageRunMode"/> argument, so one plan serves every mode.
    /// </summary>
    /// <param name="processInvoker">The CliInvoke process invoker behind spawned records.</param>
    public PlannedCommandRunner(IProcessInvoker processInvoker)
    {
        _processInvoker = processInvoker;
    }

    /// <summary>
    /// The buffered-output policy each executed process runs under. The default suits every
    /// stage; the build stage raises it so long publishes do not truncate warnings.
    /// </summary>
    public ProcessExitConfiguration ExitConfiguration { get; set; } = ProcessExitConfiguration.Default;

    /// <summary>
    /// Runs <paramref name="command"/> per <paramref name="mode"/>: under
    /// <see cref="StageRunMode.Execute"/> it prints the command line, executes the record's
    /// process or local side effect, and returns the process result (null for a local side
    /// effect); under <see cref="StageRunMode.DryRun"/> it prints the command line - marking a
    /// guarded record as conditional - and returns null without touching the work, its guards,
    /// or its diagnosis.
    /// </summary>
    /// <param name="command">The planned work item, built by the stage identically in every mode.</param>
    /// <param name="mode">The runner's decision for this item.</param>
    /// <param name="writeLine">Sink for the printed command line, in both modes; null swallows it.</param>
    /// <param name="cancellationToken">The stage's cancellation token.</param>
    /// <param name="diagnosisSink">
    /// Receives the record's diagnosis line after a verified execution. Null keeps diagnoses
    /// suppressed even when the record carries one.
    /// </param>
    /// <returns>The process result when a spawned record executed; otherwise null.</returns>
    public async Task<BufferedProcessResult?> RunAsync(
        PlannedCommand command,
        StageRunMode mode,
        Action<string>? writeLine,
        CancellationToken cancellationToken,
        Action<string>? diagnosisSink = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One string, both modes: the dry-run print and the real run's note read the same
        // record, and only the dry-run report marks a guarded item, whose guard it never runs.
        writeLine?.Invoke(
            mode == StageRunMode.DryRun && command.Condition is { } condition
                ? $"{command.CommandLine} (conditional: {condition})"
                : command.CommandLine);

        if (mode == StageRunMode.DryRun)
        {
            return null;
        }

        BufferedProcessResult? processResult = null;

        if (command.TargetFilePath is not null)
        {
            ProcessConfiguration configuration =
                new(command.TargetFilePath, command.ArgumentList)
                {
                    WorkingDirectoryPath = command.WorkingDirectory ?? Environment.CurrentDirectory,
                };

            processResult = await _processInvoker.ExecuteBufferedAsync(
                configuration, ExitConfiguration, cancellationToken).ConfigureAwait(false);
        }
        else if (command.ExecuteAsync is not null)
        {
            await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }

        // Only an executed run has verified anything, so only it may surface a diagnosis.
        if (diagnosisSink is not null && command.Diagnosis is { } diagnose)
        {
            diagnosisSink(diagnose());
        }

        return processResult;
    }
}
