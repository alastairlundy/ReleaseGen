namespace ReleaseGen.Cli.Helpers;

using System.Globalization;

/// <summary>
/// The runtime-identifier defaults behind init's interactive RID prompt. The set is
/// machine-dependent by design: the five always-on rids (<c>win-x64</c>, <c>win-arm64</c>,
/// <c>linux-x64</c>, <c>linux-arm64</c>, <c>osx-arm64</c>) join <c>osx-x64</c> when either
/// clause of the union rule holds - a declared TFM that pins a required platform version
/// older than macOS 26 (a <c>-macos.N</c> or a <c>-maccatalyst.N</c> pin, read the same way),
/// or an installed SDK whose RID catalog still lists <c>osx-x64</c>
/// [DECISIONS-ReleaseGen-adoption-friction.md#D021]. A TFM that pins nothing leaves the
/// question to the SDK clause. The portable <c>any-x64</c> and <c>any-arm64</c> rids stay
/// selectable at the prompt but are never part of the default set [#D015].
/// </summary>
public static class RidDefaults
{
    /// <summary>
    /// The macOS release whose arrival ended Intel Mac support, and the seam the TFM clause
    /// compares against. macOS and iOS left sequential majors behind at macOS 15: 26 shipped
    /// in late 2025 and 27 in late 2026, so no version between 16 and 25 exists - every
    /// pre-jump pin lands below the threshold and every model year lands at or above it.
    /// Mac Catalyst versions mirrored iOS before that jump (so a <c>-maccatalyst.18</c> pin is
    /// the macOS 15 era) and unified with macOS from 26 on, which is why the same comparison
    /// is correct for both markers with no translation between them. A TFM pinned below the
    /// threshold can still run on Intel hardware, so <c>osx-x64</c> stays a default there; a
    /// TFM pinned to this release or later is not, unless the installed SDK says otherwise.
    /// </summary>
    public const int MacOsIntelDropVersion = 26;

    /// <summary>
    /// The platform markers whose pinned version decides the TFM clause of the union rule:
    /// the macOS platform and Mac Catalyst, read the same way - a pin older than macOS 26
    /// satisfies the clause, and no pin (on either marker, or on no platform at all) leaves
    /// the question to the installed SDK's supported rids.
    /// </summary>
    private static readonly string[] MacOsFamilyPlatforms = ["-macos", "-maccatalyst"];

    /// <summary>
    /// The rids in every default set, in canonical prompt order.
    /// </summary>
    public static readonly string[] Core =
        ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64"];

    /// <summary>
    /// The rids a user may select at the prompt but that never appear in a default set:
    /// portable rids follow the zip-only Windows archive rule, so defaulting them in would
    /// quietly change archive behavior on Unix hosts.
    /// </summary>
    public static readonly string[] NeverDefault = ["any-x64", "any-arm64"];

    /// <summary>
    /// Every rid the prompt accepts, in canonical order: the default set's members first,
    /// then the conditional <c>osx-x64</c>, then the never-default portable rids.
    /// </summary>
    public static readonly string[] Selectable =
        ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64", "any-x64", "any-arm64"];

    /// <summary>
    /// The default rid set for one run: the five core rids, plus <c>osx-x64</c> when either
    /// clause of the union rule holds. The TFM clause is checked first because it needs no
    /// I/O, so the SDK probe runs whenever no declared TFM pins an older macOS version - a
    /// plain <c>net10.0</c> repository always reaches it
    /// [DECISIONS-ReleaseGen-adoption-friction.md#D021].
    /// </summary>
    /// <param name="targetFrameworks">
    /// The declared target frameworks of the projects under the root; empty when detection
    /// found or evaluated nothing.
    /// </param>
    /// <param name="installedSdkSupportsOsxX64">
    /// The installed-SDK probe; <see cref="InstalledSdkSupportsOsxX64"/> when omitted. Tests
    /// inject an answer to pin the machine out of the result.
    /// </param>
    /// <returns>The default rids, in canonical order; never containing an <c>any-*</c> rid.</returns>
    public static string[] For(
        IEnumerable<string> targetFrameworks,
        Func<bool>? installedSdkSupportsOsxX64 = null)
    {
        ArgumentNullException.ThrowIfNull(targetFrameworks);

        bool osxX64 = targetFrameworks.Any(PredatesMacOs26) ||
            (installedSdkSupportsOsxX64 ?? InstalledSdkSupportsOsxX64)();

        return osxX64 ? [.. Core, "osx-x64"] : [.. Core];
    }

    /// <summary>
    /// Whether a TFM's required platform version is pinned and predates macOS 26 - the TFM
    /// clause of the union rule. A <c>-macos.N</c> pin and a <c>-maccatalyst.N</c> pin are
    /// read the same way, with no translation between them: below the 26 seam the numbers are
    /// pre-jump versions on both markers (see <see cref="MacOsIntelDropVersion"/>). Only an
    /// explicit pin settles it: a TFM that names no such platform (<c>net10.0</c>,
    /// <c>net10.0-windows</c>) or names one without a version (<c>net10.0-macos</c>,
    /// <c>net10.0-maccatalyst</c>) states no required version at all, so it never satisfies
    /// this clause - the run then checks the installed SDK's supported rids instead
    /// [DECISIONS-ReleaseGen-adoption-friction.md#D021].
    /// </summary>
    /// <param name="targetFramework">One declared TFM, for example <c>net10.0-macos.15</c>.</param>
    /// <returns>
    /// <see langword="true"/> only when the TFM pins a macOS-family version older than macOS 26.
    /// </returns>
    public static bool PredatesMacOs26(string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(targetFramework);

        return PinnedPlatformVersion(targetFramework) is int requiredVersion &&
            requiredVersion < MacOsIntelDropVersion;
    }

    /// <summary>
    /// The version pinned by a macOS-family platform in one TFM: the digits after the
    /// platform and its version separator, as in <c>net10.0-macos.15</c> or
    /// <c>net10.0-maccatalyst.15</c>. Null when the TFM names neither platform, names one
    /// without a version, or spells the pin any other way - in every one of those cases there
    /// is no required version to compare, which is what sends the union rule to its
    /// installed-SDK clause.
    /// </summary>
    /// <param name="targetFramework">One declared TFM.</param>
    /// <returns>The pinned version, or null when the TFM pins none in the correct format.</returns>
    private static int? PinnedPlatformVersion(string targetFramework)
    {
        foreach (string platform in MacOsFamilyPlatforms)
        {
            int marker = targetFramework.IndexOf(platform, StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                continue;
            }

            int digits = marker + platform.Length;

            if (digits >= targetFramework.Length || targetFramework[digits] != '.')
            {
                // A version counts only in the correct TFM format - the platform followed by
                // '.<version>'. Without the separator there is nothing to compare.
                return null;
            }

            int start = ++digits;

            while (digits < targetFramework.Length && char.IsAsciiDigit(targetFramework[digits]))
            {
                digits++;
            }

            if (digits == start)
            {
                // The separator is there but no version digits follow it.
                return null;
            }

            return int.TryParse(
                       targetFramework.AsSpan(start, digits - start),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out int version)
                ? version
                : (int?)null;
        }

        return null;
    }

    /// <summary>
    /// Whether any installed SDK's RID catalog still lists <c>osx-x64</c> - the installed-SDK
    /// clause of the union rule. The probe reads <c>RuntimeIdentifierGraph.json</c> from each
    /// <c>sdk/&lt;version&gt;</c> folder of the dotnet root (declared via
    /// <c>DOTNET_ROOT</c>, otherwise the root the running runtime lives under), so the answer
    /// can change when the SDK is upgraded or an Intel macOS rid is retired from the catalog
    /// [DECISIONS-ReleaseGen-adoption-friction.md#D021]. A missing or unreadable catalog is
    /// not support: the clause then simply does not hold and the TFM clause decides alone.
    /// </summary>
    /// <returns><see langword="true"/> when at least one installed SDK ships an osx-x64 catalog entry.</returns>
    public static bool InstalledSdkSupportsOsxX64()
    {
        foreach (string graphFile in RidGraphFiles())
        {
            try
            {
                if (File.ReadAllText(graphFile).Contains("\"osx-x64\"", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // One unreadable catalog says nothing about the others; keep looking.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    /// <summary>
    /// The RID catalog files of every installed SDK under the dotnet root: one
    /// <c>RuntimeIdentifierGraph.json</c> per <c>sdk/&lt;version&gt;</c> folder. The root
    /// comes from <c>DOTNET_ROOT</c> when the machine declares one, and otherwise from the
    /// layout the running runtime itself lives in (<c>&lt;root&gt;/shared/…</c>), so a plain
    /// <c>dotnet</c> install is found without asking the muxer. Any lookup failure - no root,
    /// no sdk folder, no access - answers with nothing, which reads as "the SDK clause does
    /// not hold" rather than as an error.
    /// </summary>
    /// <returns>The catalog file paths that exist; empty when none can be found.</returns>
    private static string[] RidGraphFiles()
    {
        string dotnetRoot = DotNetRoot();

        if (string.IsNullOrEmpty(dotnetRoot))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory
                    .EnumerateDirectories(Path.Combine(dotnetRoot, "sdk"))
                    .Select(directory => Path.Combine(directory, "RuntimeIdentifierGraph.json"))
                    .Where(File.Exists),
            ];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The dotnet installation root holding the <c>sdk</c> folder: the <c>DOTNET_ROOT</c>
    /// environment variable when set, otherwise the root the running runtime's own directory
    /// sits three levels below (<c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>),
    /// confirmed by the <c>sdk</c> folder it must contain. An empty answer means no root could
    /// be determined, and the caller treats that as no evidence of rid support.
    /// </summary>
    /// <returns>The dotnet root path, or an empty string when it cannot be determined.</returns>
    private static string DotNetRoot()
    {
        string? declared = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (!string.IsNullOrEmpty(declared))
        {
            return declared;
        }

        string? runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);

        if (string.IsNullOrEmpty(runtimeDirectory))
        {
            return string.Empty;
        }

        DirectoryInfo? candidate = new(runtimeDirectory);

        for (int depth = 0; depth < 3 && candidate is not null; depth++)
        {
            candidate = candidate.Parent;
        }

        return candidate is not null &&
               Directory.Exists(Path.Combine(candidate.FullName, "sdk"))
            ? candidate.FullName
            : string.Empty;
    }
}
