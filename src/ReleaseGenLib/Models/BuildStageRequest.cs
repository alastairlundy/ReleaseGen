namespace ReleaseGenLib.Models;

/// <summary>
/// The resolved inputs for one build-stage invocation: the loaded manifest, the
/// canonical preset name in force (flag override already applied by the CLI, never written back
/// to the manifest), and the local output locations. Output stays local-only - binaries and
/// archives are written under <see cref="OutputRoot"/> and never uploaded.
/// </summary>
public sealed class BuildStageRequest
{
    /// <summary>
    /// The loaded and validated manifest driving the stage.
    /// </summary>
    public required Manifest Manifest { get; init; }

    /// <summary>
    /// The manifest's directory. Relative target project paths resolve from here, matching the
    /// manifest loader's resolution rule.
    /// </summary>
    public required DirectoryInfo ManifestDirectory { get; init; }

    /// <summary>
    /// The canonical preset name in force for this invocation: one of
    /// <see cref="BuildPreset.All"/>, already validated by the caller.
    /// </summary>
    public required string Preset { get; init; }

    /// <summary>
    /// The local root the stage writes under: per-rid folders and archives. The default
    /// is a <c>release</c> directory under the invocation directory; the layout is documented on
    /// <see cref="BuildStageRunner"/>.
    /// </summary>
    public required DirectoryInfo OutputRoot { get; init; }

    /// <summary>
    /// Sink for publish stdout lines, routed by the CLI through the conventions layer.
    /// Null swallows the lines.
    /// </summary>
    public Action<string>? WriteLine { get; init; }

    /// <summary>
    /// Sink for publish stderr lines, routed by the CLI as warnings so they stay visible under
    /// <c>--quiet</c>. Null swallows the lines.
    /// </summary>
    public Action<string>? WriteErrorLine { get; init; }

    /// <summary>
    /// Sink for a planned record's diagnosis line, surfaced by the runner only after a real
    /// run has executed the record; the CLI routes it to the cause line, which prints under
    /// <c>--verbose</c> only. Null keeps diagnoses suppressed even when a record carries one.
    /// </summary>
    public Action<string>? WriteDiagnosisLine { get; init; }

    /// <summary>
    /// The runner's decision for this invocation: execute the stage's planned commands, or
    /// print them (the dry-run report) without running them, their guards, or the stale-output
    /// cleanup. Defaults to a real run; the dry-run flag wires it.
    /// </summary>
    public StageRunMode RunMode { get; init; } = StageRunMode.Execute;
}
