namespace ReleaseGen.Tests;

using ReleaseGen.Cli.Helpers;

/// <summary>
/// The rid default set behind the interactive prompt: the five core rids, the union rule's
/// two clauses for <c>osx-x64</c> (a TFM pinning a required platform version older than
/// macOS 26 - <c>-macos.N</c> or <c>-maccatalyst.N</c> - or an installed SDK whose RID catalog
/// still carries the rid; a TFM that pins nothing defers to the latter), the TFM-version
/// branch in both directions, and the invariant that the portable <c>any-*</c> rids are
/// selectable but never default. The SDK probe is injected per case, so no assertion depends
/// on the machine.
/// </summary>
public class RidDefaultsTests
{
    private static readonly string[] Core =
        ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64"];

    [Test]
    public async Task A_tfm_requiring_macos_older_than_26_defaults_osx_x64_even_without_sdk_support()
    {
        string[] rids = RidDefaults.For(["net10.0-macos.15"], () => false);

        await Assert.That(rids).IsEquivalentTo([.. Core, "osx-x64"]);
    }

    [Test]
    public async Task A_tfm_requiring_macos_26_or_newer_keeps_osx_x64_out_without_sdk_support()
    {
        await Assert.That(RidDefaults.For(["net10.0-macos.26"], () => false))
            .IsEquivalentTo(Core);
        await Assert.That(RidDefaults.For(["net10.0-macos.27"], () => false))
            .IsEquivalentTo(Core);
    }

    [Test]
    public async Task The_sdk_clause_lights_osx_x64_up_when_the_tfm_clause_does_not()
    {
        string[] rids = RidDefaults.For(["net10.0-macos.26"], () => true);

        await Assert.That(rids).IsEquivalentTo([.. Core, "osx-x64"]);
    }

    [Test]
    public async Task A_tfm_without_a_macos_pin_falls_through_to_the_sdk_clause()
    {
        // Neither net10.0 nor an unpinned maccatalyst platform states a required version, so
        // the TFM clause says nothing and the installed SDK's supported rids decide [D021].
        await Assert.That(RidDefaults.For(["net10.0"], () => false)).IsEquivalentTo(Core);
        await Assert.That(RidDefaults.For(["net10.0"], () => true))
            .IsEquivalentTo([.. Core, "osx-x64"]);
        await Assert.That(RidDefaults.For(["net10.0-maccatalyst"], () => false))
            .IsEquivalentTo(Core);
        await Assert.That(RidDefaults.For(["net10.0-maccatalyst"], () => true))
            .IsEquivalentTo([.. Core, "osx-x64"]);
    }

    [Test]
    public async Task A_maccatalyst_pin_is_read_exactly_like_a_macos_pin()
    {
        await Assert.That(RidDefaults.For(["net10.0-maccatalyst.15"], () => false))
            .IsEquivalentTo([.. Core, "osx-x64"]);
        await Assert.That(RidDefaults.For(["net10.0-maccatalyst.26"], () => false))
            .IsEquivalentTo(Core);
        await Assert.That(RidDefaults.For(["net10.0-maccatalyst.26"], () => true))
            .IsEquivalentTo([.. Core, "osx-x64"]);
    }

    [Test]
    public async Task Without_any_tfm_the_sdk_clause_decides_alone()
    {
        await Assert.That(RidDefaults.For([], () => false)).IsEquivalentTo(Core);
        await Assert.That(RidDefaults.For([], () => true)).IsEquivalentTo([.. Core, "osx-x64"]);
    }

    [Test]
    public async Task One_legacy_tfm_among_several_is_enough_for_osx_x64()
    {
        string[] rids = RidDefaults.For(
            ["net10.0-macos.26", "net10.0-macos.15"], () => false);

        await Assert.That(rids).IsEquivalentTo([.. Core, "osx-x64"]);
    }

    [Test]
    public async Task The_portable_any_rids_are_selectable_but_never_default()
    {
        await Assert.That(RidDefaults.NeverDefault).IsEquivalentTo(["any-x64", "any-arm64"]);
        await Assert.That(RidDefaults.Selectable).IsEquivalentTo(
            ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64", "any-x64", "any-arm64"]);

        // Every shape the default rule can produce, none of them carrying a portable rid.
        string[][] sets =
        [
            RidDefaults.For([], () => false),
            RidDefaults.For([], () => true),
            RidDefaults.For(["net10.0"], () => false),
            RidDefaults.For(["net10.0-macos.26"], () => true),
        ];

        foreach (string[] rids in sets)
        {
            await Assert.That(rids.Contains("any-x64", StringComparer.Ordinal)).IsFalse();
            await Assert.That(rids.Contains("any-arm64", StringComparer.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task The_tfm_version_branch_reads_only_the_pinned_platform_version()
    {
        (string Tfm, bool Predates)[] cases =
        [
            // Pre-jump pins satisfy the clause: macOS went 15 straight to 26, and Mac
            // Catalyst mirrored iOS below that jump - so maccatalyst.18 is the macOS 15 era,
            // not a version anywhere near 26.
            ("net10.0-macos.14", true),
            ("net10.0-macos.15", true),
            ("net10.0-maccatalyst.15", true),
            ("net10.0-maccatalyst.18", true),

            // The model years sit at or past the Intel drop, on both markers.
            ("net10.0-macos.26", false),
            ("net10.0-macos.27", false),
            ("net10.0-maccatalyst.26", false),

            // No pin, or no macOS-family platform at all: no required version to compare, so
            // the clause never holds here and the installed-SDK clause decides instead.
            ("net10.0-macos", false),
            ("net10.0-maccatalyst", false),
            ("net10.0", false),
            ("net10.0-windows", false),
            ("net10.0-ios", false),

            // Not the correct TFM format - no version separator - so it pins nothing either.
            ("net10.0-macos15", false),
        ];

        foreach ((string tfm, bool predates) in cases)
        {
            await Assert.That(RidDefaults.PredatesMacOs26(tfm))
                .IsEqualTo(predates);
        }
    }

    [Test]
    public async Task The_installed_sdk_probe_is_stable_and_never_throws()
    {
        // The real probe reads whatever catalogs this machine has; the contract it must keep
        // is that asking twice gives the same answer, so the default set cannot flap within
        // one run.
        bool first = RidDefaults.InstalledSdkSupportsOsxX64();
        bool second = RidDefaults.InstalledSdkSupportsOsxX64();

        await Assert.That(first).IsEqualTo(second);
    }
}
