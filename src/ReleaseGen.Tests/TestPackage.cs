namespace ReleaseGen.Tests;

using System.IO.Compression;

/// <summary>
/// Builds the hand-crafted <c>.nupkg</c> fixture the publish integration test stages as the
/// package stage's local product: a zip carrying a valid nuspec, which is all
/// <c>dotnet nuget push</c> reads when the destination is a local folder source.
/// </summary>
internal static class TestPackage
{
    /// <summary>
    /// Writes a minimal but valid package to <paramref name="packagePath"/>.
    /// </summary>
    /// <param name="packagePath">The full path of the .nupkg to create.</param>
    /// <param name="id">The package id, which must match the file naming NuGet expects.</param>
    /// <param name="version">The package version.</param>
    public static void WriteNupkg(string packagePath, string id, string version)
    {
        string? directory = Path.GetDirectoryName(packagePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string nuspec =
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{id}}</id>
                <version>{{version}}</version>
                <authors>ReleaseGen.Tests</authors>
                <description>Local-source push fixture for ReleaseGen's own suite.</description>
              </metadata>
            </package>
            """;

        using FileStream stream = new(packagePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry($"{id}.nuspec");

        using (StreamWriter writer = new(entry.Open()))
        {
            writer.Write(nuspec);
        }
    }
}
