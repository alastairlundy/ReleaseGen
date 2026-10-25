namespace ReleaseGenLib;

/// <summary>
/// One work item a stage would perform, described once so a real run, the dry-run report, and a
/// verbose trace all read the same record: the command line both printed modes show, the
/// artifacts the item touches, an optional runtime guard reported but never evaluated when
/// printed, an optional local side effect for work that is not a spawned process, and an
/// optional diagnosis callback that only a verified execution may surface. Stages build these
/// records without knowing the <see cref="StageRunMode"/>; the runner decides whether to
/// execute the record or print it, so the plan cannot diverge across flags.
/// </summary>
public sealed class PlannedCommand
{
    /// <summary>
    /// The full command line exactly as both printed modes show it, redacted where a secret
    /// would otherwise reach the console. Both the dry-run print and the real run's note are
    /// this one string.
    /// </summary>
    public required string CommandLine { get; init; }

    /// <summary>
    /// The spawned process's executable path, or null for a local side effect carried by
    /// <see cref="ExecuteAsync"/>. With <see cref="TargetFilePath"/> set, the runner spawns the
    /// process through CliInvoke when it executes.
    /// </summary>
    public string? TargetFilePath { get; init; }

    /// <summary>
    /// The spawned process's argument list, exactly as the runner passes it. Never printed -
    /// <see cref="CommandLine"/> is the printed form.
    /// </summary>
    public IReadOnlyList<string> ArgumentList { get; init; } = [];

    /// <summary>
    /// The spawned process's working directory; null inherits the runner's current directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// A local side effect instead of a spawned process, such as the archiver writing one
    /// archive. The runner invokes it only when it executes.
    /// </summary>
    public Func<CancellationToken, Task>? ExecuteAsync { get; init; }

    /// <summary>
    /// The artifact paths this work item writes or pushes, named in the plan. The dry-run
    /// report describes them; nothing writes them.
    /// </summary>
    public IReadOnlyList<string> ArtifactPaths { get; init; } = [];

    /// <summary>
    /// A runtime guard the work evaluates when it runs, in its own words - for example, the
    /// archiver's source-folder check. The runner never evaluates it: the dry-run print reports
    /// it as conditional instead, so the report may describe work a real run would skip.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// An optional diagnosis of what this command means when it fails or misbehaves, produced
    /// only after execution has verified something; the runner invokes it then and only when a
    /// diagnosis sink surfaces it. The dry-run report never invokes it.
    /// </summary>
    public Func<string>? Diagnosis { get; init; }
}
