namespace ReleaseGen.Tests;

using ReleaseGen.Cli.Resolvers;
using ReleaseGenLib.Models;

/// <summary>
/// The skip-duplicates adapter truth table: the CLI-side resolver folds the manifest's
/// publish section and the parsed <c>--skip-duplicate</c> switch through the library's pure
/// merge. The resolver is a thin pass-through with no parsing and no
/// console, so these tests stay parallel-safe like the other unit suites.
/// </summary>
public class NuGetSkipResolverTests
{
    private const string CliWins =
        "skipDuplicates conflict: --skip-duplicate (command line) wins over publish.skipDuplicates";

    [Test]
    public async Task Both_channels_absent_disable_skipping_without_conflict()
    {
        SkipDuplicatesDecision decision = NuGetSkipResolver.Resolve(null, null);

        // Absent on both sides is the fail-fast default, exactly as today.
        await Assert.That(decision.SkipEnabled).IsFalse();
        await Assert.That(decision.ConflictMessage).IsNull();
    }

    [Test]
    public async Task An_explicit_manifest_value_alone_supplies_the_decision()
    {
        SkipDuplicatesDecision enabled = NuGetSkipResolver.Resolve(Section(true), null);
        SkipDuplicatesDecision disabled = NuGetSkipResolver.Resolve(Section(false), null);

        // One explicit channel is never a conflict, whichever value it carries.
        await Assert.That(enabled.SkipEnabled).IsTrue();
        await Assert.That(enabled.ConflictMessage).IsNull();
        await Assert.That(disabled.SkipEnabled).IsFalse();
        await Assert.That(disabled.ConflictMessage).IsNull();
    }

    [Test]
    public async Task An_explicit_cli_value_alone_supplies_the_decision()
    {
        SkipDuplicatesDecision enabled = NuGetSkipResolver.Resolve(null, true);
        SkipDuplicatesDecision disabled = NuGetSkipResolver.Resolve(null, false);

        // An absent manifest channel is never a conflict.
        await Assert.That(enabled.SkipEnabled).IsTrue();
        await Assert.That(enabled.ConflictMessage).IsNull();
        await Assert.That(disabled.SkipEnabled).IsFalse();
        await Assert.That(disabled.ConflictMessage).IsNull();
    }

    [Test]
    public async Task Equal_explicit_values_agree_without_conflict()
    {
        SkipDuplicatesDecision bothTrue = NuGetSkipResolver.Resolve(Section(true), true);
        SkipDuplicatesDecision bothFalse = NuGetSkipResolver.Resolve(Section(false), false);

        // Agreement is not a conflict: only explicit differences are.
        await Assert.That(bothTrue.SkipEnabled).IsTrue();
        await Assert.That(bothTrue.ConflictMessage).IsNull();
        await Assert.That(bothFalse.SkipEnabled).IsFalse();
        await Assert.That(bothFalse.ConflictMessage).IsNull();
    }

    [Test]
    public async Task Differing_explicit_values_pick_the_cli_winner_with_a_conflict_message()
    {
        SkipDuplicatesDecision cliOn = NuGetSkipResolver.Resolve(Section(false), true);
        SkipDuplicatesDecision cliOff = NuGetSkipResolver.Resolve(Section(true), false);

        // Both channels explicit and differing: the CLI value wins and the message names
        // it, in both directions.
        await Assert.That(cliOn.SkipEnabled).IsTrue();
        await Assert.That(cliOn.ConflictMessage).IsEqualTo(CliWins);
        await Assert.That(cliOff.SkipEnabled).IsFalse();
        await Assert.That(cliOff.ConflictMessage).IsEqualTo(CliWins);
    }

    [Test]
    public async Task Every_channel_pair_returns_exactly_the_library_merge_output()
    {
        ManifestPublishSection?[] manifestSections = [null, Section(null), Section(true), Section(false)];
        bool?[] cliValues = [null, true, false];

        // The adapter is a thin consumer: its decision is SkipDuplicatesDecision.Merge's
        // output for both channels, mapping an absent switch to null.
        foreach (ManifestPublishSection? section in manifestSections)
        {
            foreach (bool? cliValue in cliValues)
            {
                SkipDuplicatesDecision decision = NuGetSkipResolver.Resolve(section, cliValue);
                SkipDuplicatesDecision merged = SkipDuplicatesDecision.Merge(section?.SkipDuplicates, cliValue);

                await Assert.That(decision).IsEqualTo(merged);
            }
        }
    }

    /// <summary>
    /// A publish section carrying the given explicit manifest-channel value; null leaves the
    /// <c>skipDuplicates</c> key absent, exactly like a manifest that never declared it.
    /// </summary>
    private static ManifestPublishSection Section(bool? skipDuplicates) =>
        new() { SkipDuplicates = skipDuplicates };
}
