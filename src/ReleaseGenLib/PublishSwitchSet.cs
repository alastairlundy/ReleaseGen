namespace ReleaseGenLib;

/// <summary>
/// The fully resolved dotnet publish switch set for one (target, rid) publish.
/// Preset defaults are resolved first, then any tri-state key present in the manifest's build
/// section overrides its preset default; preset none contributes nothing implicit.
/// </summary>
/// <param name="PublishAot">Whether <c>--publish-aot</c> is in force.</param>
/// <param name="PublishTrimmed">Whether trimming is in force (rendered only when AOT is not, since AOT implies trimming).</param>
/// <param name="PublishReadyToRun">Whether ready-to-run compilation is in force.</param>
/// <param name="SelfContained">Self-contained state; null means no <c>--self-contained</c> switch (preset none with no manifest key).</param>
/// <param name="RollForward">Roll-forward policy for framework-dependent publishes; null means no switch.</param>
/// <param name="PublishSingleFile">Single-file state; null means no switch - the default-off case leaves the project file's own setting alone.</param>
public sealed record PublishSwitchSet(
    bool PublishAot,
    bool PublishTrimmed,
    bool PublishReadyToRun,
    bool? SelfContained,
    string? RollForward,
    bool? PublishSingleFile)
{
    /// <summary>
    /// Renders the switch set as dotnet publish arguments, in a stable order. Only enabling
    /// switches are emitted for the boolean build flags (plus the explicitly valued
    /// <c>--self-contained</c> and single-file switches), so a project file's own settings stay
    /// authoritative whenever ReleaseGen has no opinion.
    /// </summary>
    /// <returns>The publish switches for this (target, rid).</returns>
    public IReadOnlyList<string> ToArguments()
    {
        List<string> arguments = [];

        if (PublishAot)
        {
            // Property form, not the nonexistent `--publish-aot` CLI switch: dotnet publish has
            // no aot switch (--help lists none) and forwards unknown switches to MSBuild, which
            // rejects them with MSB1001. PublishAot is the SDK property behind AOT publishing.
            arguments.Add("-p:PublishAot=true");
        }

        // AOT implies trimming: never a separate trimming switch alongside publish-aot.
        if (PublishTrimmed && !PublishAot)
        {
            arguments.Add("-p:PublishTrimmed=true");
        }

        if (PublishReadyToRun)
        {
            arguments.Add("-p:PublishReadyToRun=true");
        }

        if (SelfContained is { } selfContained)
        {
            arguments.Add($"--self-contained={(selfContained ? "true" : "false")}");
        }

        if (RollForward is not null)
        {
            arguments.Add($"-p:RollForward={RollForward}");
        }

        if (PublishSingleFile is { } singleFile)
        {
            arguments.Add($"-p:PublishSingleFile={(singleFile ? "true" : "false")}");
        }

        return arguments;
    }
}