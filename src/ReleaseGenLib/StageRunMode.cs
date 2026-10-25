namespace ReleaseGenLib;

/// <summary>
/// The runner's decision at its boundary: execute a stage's planned commands, or print them
/// (the dry-run report) without running them or their runtime guards. Stages build their
/// <see cref="PlannedCommand"/> records without consulting the mode, so every mode reads one
/// plan and the printed lines cannot diverge across flags.
/// </summary>
public enum StageRunMode
{
    /// <summary>
    /// A real run: the runner prints each planned command's line, executes it, and returns its
    /// process result.
    /// </summary>
    Execute,

    /// <summary>
    /// The dry-run report: the runner prints each planned command's line - marking guarded
    /// side effects as conditional - and runs nothing, so no guard is evaluated and no
    /// diagnosis text, which nothing has verified, can appear.
    /// </summary>
    DryRun,
}
