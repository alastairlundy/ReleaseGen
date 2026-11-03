namespace ReleaseGen.Tests;

using ReleaseGenLib.Models;

/// <summary>
/// The skip-duplicates merge truth table: the Lib-owned value object that folds the
/// manifest's publish.skipDuplicates default with the CLI's --skip-duplicate switch
/// into one decision. The type is pure - no parsing, no console,
/// no working-directory access - so these tests are parallel-safe like the
/// other unit suites.
/// </summary>
public class SkipDuplicatesMergeTests
{
    private const string CliWins =
        "skipDuplicates conflict: --skip-duplicate (command line) wins over publish.skipDuplicates";

    [Test]
    public async Task Both_channels_absent_disable_skipping_without_conflict()
    {
        SkipDuplicatesDecision decision = SkipDuplicatesDecision.Merge(null, null);

        // Absent on both sides is the fail-fast default, exactly as today.
        await Assert.That(decision.SkipEnabled).IsFalse();
        await Assert.That(decision.ConflictMessage).IsNull();
    }

    [Test]
    public async Task An_explicit_manifest_value_alone_supplies_the_decision()
    {
        SkipDuplicatesDecision enabled = SkipDuplicatesDecision.Merge(true, null);
        SkipDuplicatesDecision disabled = SkipDuplicatesDecision.Merge(false, null);

        // One explicit channel is never a conflict, whichever value it carries.
        await Assert.That(enabled.SkipEnabled).IsTrue();
        await Assert.That(enabled.ConflictMessage).IsNull();
        await Assert.That(disabled.SkipEnabled).IsFalse();
        await Assert.That(disabled.ConflictMessage).IsNull();
    }

    [Test]
    public async Task An_explicit_cli_value_alone_supplies_the_decision()
    {
        SkipDuplicatesDecision enabled = SkipDuplicatesDecision.Merge(null, true);
        SkipDuplicatesDecision disabled = SkipDuplicatesDecision.Merge(null, false);

        // An absent CLI switch is never a conflict.
        await Assert.That(enabled.SkipEnabled).IsTrue();
        await Assert.That(enabled.ConflictMessage).IsNull();
        await Assert.That(disabled.SkipEnabled).IsFalse();
        await Assert.That(disabled.ConflictMessage).IsNull();
    }

    [Test]
    public async Task Equal_explicit_values_agree_without_conflict()
    {
        SkipDuplicatesDecision bothTrue = SkipDuplicatesDecision.Merge(true, true);
        SkipDuplicatesDecision bothFalse = SkipDuplicatesDecision.Merge(false, false);

        // Agreement is not a conflict: only explicit differences are.
        await Assert.That(bothTrue.SkipEnabled).IsTrue();
        await Assert.That(bothTrue.ConflictMessage).IsNull();
        await Assert.That(bothFalse.SkipEnabled).IsFalse();
        await Assert.That(bothFalse.ConflictMessage).IsNull();
    }

    [Test]
    public async Task Differing_explicit_values_pick_the_cli_winner_with_a_conflict_message()
    {
        SkipDuplicatesDecision cliOff = SkipDuplicatesDecision.Merge(true, false);
        SkipDuplicatesDecision cliOn = SkipDuplicatesDecision.Merge(false, true);

        // Both channels explicit and differing: the CLI value wins and the message
        // names it, in both directions.
        await Assert.That(cliOff.SkipEnabled).IsFalse();
        await Assert.That(cliOff.ConflictMessage).IsEqualTo(CliWins);
        await Assert.That(cliOn.SkipEnabled).IsTrue();
        await Assert.That(cliOn.ConflictMessage).IsEqualTo(CliWins);
    }
}
