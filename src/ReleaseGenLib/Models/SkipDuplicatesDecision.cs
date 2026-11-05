namespace ReleaseGenLib.Models;

/// <summary>
/// The skip-duplicates decision: whether duplicate pushes are skipped, plus an optional
/// conflict message for when the manifest and CLI channels disagreed. One immutable
/// value object with a pure <see cref="Merge"/> - no parsing, no console - so the CLI
/// adapter stays a thin consumer and the rules test without processes or the
/// working-directory lock.
/// </summary>
public sealed record SkipDuplicatesDecision(bool SkipEnabled, string? ConflictMessage)
{
    /// <summary>
    /// The conflict wording, naming both channels and the CLI winner. The adapter
    /// writes it to standard error at most once per run.
    /// </summary>
    private const string CliWinsMessage =
        "skipDuplicates conflict: --skip-duplicate (command line) wins over publish.skipDuplicates";

    /// <summary>
    /// Folds the manifest's publish.skipDuplicates default with the CLI's
    /// --skip-duplicate switch: an explicit CLI value wins over an explicit manifest
    /// value, an absent channel falls through to the other, and both absent means
    /// skipping is off (today's fail-fast default). A conflict message appears only
    /// when both channels are explicit and differ - equal values never conflict.
    /// </summary>
    public static SkipDuplicatesDecision Merge(bool? manifestEnabled, bool? cliEnabled)
    {
        bool skipEnabled = cliEnabled ?? manifestEnabled ?? false;
        bool conflict = manifestEnabled is not null
            && cliEnabled is not null
            && manifestEnabled != cliEnabled;

        return new SkipDuplicatesDecision(skipEnabled, conflict ? CliWinsMessage : null);
    }
}
