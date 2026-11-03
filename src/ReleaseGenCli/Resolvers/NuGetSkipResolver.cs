namespace ReleaseGen.Cli.Resolvers;

/// <summary>
/// Resolves the publish stage's skip-duplicates decision: the thin adapter that feeds the
/// manifest's publish section and the parsed <c>--skip-duplicate</c> switch into the library's
/// pure <see cref="SkipDuplicatesDecision.Merge(bool?, bool?)"/>. The adapter carries no rules
/// of its own and writes nothing to the console: the stage prints the decision's conflict
/// message at its single resolution call site, which is what makes the once-per-run cap
/// mechanical.
/// </summary>
public static class NuGetSkipResolver
{
    /// <summary>
    /// Folds the two channels into one decision: an explicit CLI switch value wins over an
    /// explicit manifest value, an absent channel falls through to the other, and both absent
    /// means skipping is off. The manifest channel is the publish section's
    /// <c>skipDuplicates</c> key - null when the key is absent - so "declared off" stays
    /// distinguishable from "never declared" all the way to the merge.
    /// </summary>
    /// <param name="publishSection">The loaded manifest's publish section; its <c>skipDuplicates</c> key supplies the manifest channel, and a null key or a null section reads as an absent channel.</param>
    /// <param name="cliEnabled">The parsed <c>--skip-duplicate</c> switch value; null when the switch is absent.</param>
    /// <returns>The merged decision: whether skipping is enabled, plus the conflict message when both channels are explicit and differ.</returns>
    public static SkipDuplicatesDecision Resolve(ManifestPublishSection? publishSection, bool? cliEnabled)
    {
        return SkipDuplicatesDecision.Merge(publishSection?.SkipDuplicates, cliEnabled);
    }
}
