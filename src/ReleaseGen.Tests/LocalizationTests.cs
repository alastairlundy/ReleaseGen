using System.Reflection;
using ReleaseGen.Cli.Resolvers;

namespace ReleaseGen.Tests;

/// <summary>
/// The localization contract: the generated Resources class asks its assembly for a manifest
/// named after the project's RootNamespace plus the Localizations folder, which is the name
/// Resources.resx is embedded under. A RootNamespace rename leaves the two names disagreeing,
/// so every localized string throws MissingManifestResourceException at the lookup site — a
/// failure no other test reaches unless it happens to call the throw path.
/// </summary>
public class LocalizationTests
{
    [Test]
    public async Task Every_localized_string_resolves_from_the_embedded_manifest()
    {
        Assembly cli = typeof(NuGetSourceResolver).Assembly;
        Type resources = cli.GetTypes().Single(
            type => type.Name == "Resources"
                && type.Namespace is not null
                && type.Namespace.EndsWith(".Localizations", StringComparison.Ordinal));

        PropertyInfo[] messages = resources
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .ToArray();

        await Assert.That(messages).IsNotEmpty();

        foreach (PropertyInfo message in messages)
        {
            string value = (string)message.GetValue(null)!;

            await Assert.That(value).IsNotEmpty();
        }
    }
}
