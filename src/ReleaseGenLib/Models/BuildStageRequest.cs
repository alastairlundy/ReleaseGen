namespace ReleaseGenLib.Models;

/// <summary>
/// The resolved inputs for one build-stage invocation (T009, T013): the loaded manifest, the
/// canonical preset name in force (flag override already applied by the CLI, never written back
/// to the manifest), and the local output locations. Output stays local-only - binaries and
/// archives are written under <see cref="OutputRoot"/> and never uploaded (D012).
/// </summary>
public sealed class BuildStageRequest
{
    /// <summary>
    /// The loaded and validated manifest driving the stage (D002).
    /// </summary>
    public required Manifest Manifest { get; init; }

    /// <summary>
    /// The manifest's directory. Relative target project paths resolve from here, matching the
    /// manifest loader's resolution rule (D009).
    /// </summary>
    public required DirectoryInfo ManifestDirectory { get; init; }

    /// <summary>
    /// The canonical preset name in force for this invocation (T013): one of
    /// <see cref="BuildPreset.All"/>, already validated by the caller.
    /// </summary>
    public required string Preset { get; init; }

    /// <summary>
    /// The local root the stage writes under: per-rid folders and archives (D012). The default
    /// is a <c>release</c> directory under the invocation directory; the layout is documented on
    /// <see cref="BuildStageRunner"/>.
    /// </summary>
    public required DirectoryInfo OutputRoot { get; init; }

    /// <summary>
    /// Sink for publish stdout lines, routed by the CLI through the conventions layer (T017).
    /// Null swallows the lines.
    /// </summary>
    public Action<string>? WriteLine { get; init; }

    /// <summary>
    /// Sink for publish stderr lines, routed by the CLI as warnings so they stay visible under
    /// <c>--quiet</c> (T017). Null swallows the lines.
    /// </summary>
    public Action<string>? WriteErrorLine { get; init; }
}
