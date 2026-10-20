namespace ReleaseGenCli.Commands;

/// <summary>
/// Resolves the publish stage's push sources: the manifest's listed sources win in
/// manifest order, and a manifest listing none resolves to the nuget.org default. Sources are
/// the only publish-section data in v1 - credentials never live on disk; the API key
/// travels through the environment or a CLI option only. A resolved source is handed to
/// <c>dotnet nuget push</c> verbatim, so a URL feed and a local folder source (the
/// integration-test destination) resolve the same way.
/// </summary>
public static class NuGetSourceResolver
{
    /// <summary>
    /// The nuget.org v3 feed: the default destination when the manifest lists no sources.
    /// </summary>
    public const string DefaultSource = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// Resolves the push sources: the manifest's listed sources when it carries any, otherwise
    /// the nuget.org default. A blank entry fails with an error naming its position
    /// rather than being silently dropped (the fail-loudly posture).
    /// </summary>
    /// <param name="manifestSources">The manifest publish section's <c>sources</c> array.</param>
    /// <returns>The resolved sources; never empty.</returns>
    /// <exception cref="ArgumentException">The manifest list carries a blank source entry.</exception>
    public static string[] Resolve(string[] manifestSources)
    {
        ArgumentNullException.ThrowIfNull(manifestSources);

        if (manifestSources.Length == 0)
        {
            return [DefaultSource];
        }

        for (int i = 0; i < manifestSources.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(manifestSources[i]))
            {
                // The error style: names the offending entry by its position.
                throw new ArgumentException(
                    $"publish: the manifest's publish.sources entry at position {i} is empty.",
                    nameof(manifestSources));
            }
        }

        return manifestSources;
    }
}
