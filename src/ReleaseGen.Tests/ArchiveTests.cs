using ReleaseGenLib.Exceptions;

namespace ReleaseGen.Tests;

using System.Formats.Tar;
using System.IO.Compression;
using ReleaseGenCli.Commands;
using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// Archive creation: zip is the executable-kind default, tar.gz and none are the declared
/// opt-outs, package kinds never archive, archives replace any stale file, and an unknown format
/// fails naming the known set. Output stays local under <c>release/</c>. The command-level
/// tests drive the build stage through the test's own DI scope; the pure archive tests need
/// neither injected service.
/// </summary>
[ReleaseGenDi]
public class ArchiveTests(IServiceProvider services, IBuildArchiver archiver, RecordingProcessInvoker invoker)
{
    [Test]
    public async Task A_zip_archive_carries_the_folders_contents_with_relative_names()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace, out string[] expectedEntries);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-win-x64.zip"));

        await archiver.CreateArchiveAsync(source, archive, ArchiveFormats.Zip, CancellationToken.None);

        await Assert.That(File.Exists(archive.FullName)).IsTrue();

        await using ZipArchive zip = await ZipFile.OpenReadAsync(archive.FullName);
        string[] entries = [.. zip.Entries.Select(static entry => entry.FullName)];

        await Assert.That(entries).IsEquivalentTo(expectedEntries);
    }

    [Test]
    public async Task A_tar_gz_archive_carries_the_folders_contents_with_relative_names()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace, out string[] expectedEntries);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-win-x64.tar.gz"));

        await archiver.CreateArchiveAsync(source, archive, ArchiveFormats.TarGz, CancellationToken.None);

        await Assert.That(File.Exists(archive.FullName)).IsTrue();

        await using FileStream stream = File.OpenRead(archive.FullName);
        await using GZipStream gzip = new(stream, CompressionMode.Decompress);
        await using TarReader reader = new(gzip);

        List<string> entries = [];

        while (await reader.GetNextEntryAsync() is { } entry)
        {
            entries.Add(entry.Name);
        }

        await Assert.That(entries).IsEquivalentTo(expectedEntries);
    }

    [Test]
    public async Task The_none_format_writes_no_archive()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace, out _);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-win-x64.zip"));

        await archiver.CreateArchiveAsync(source, archive, ArchiveFormats.None, CancellationToken.None);

        await Assert.That(File.Exists(archive.FullName)).IsFalse();
    }

    [Test]
    public async Task An_unknown_format_fails_naming_the_known_set()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace, out _);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-win-x64.rar"));

        ManifestException exception = await Capture.ExceptionAsync<ManifestException>(
            () => archiver.CreateArchiveAsync(source, archive, "rar", CancellationToken.None));

        await Assert.That(exception.Message).Contains("Unknown archive format 'rar'");
        await Assert.That(exception.Message).Contains("zip, tar.gz, none");
    }

    [Test]
    public async Task A_missing_source_folder_fails_naming_both_paths()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = new(Path.Combine(workspace.Root, "does-not-exist"));
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-win-x64.zip"));

        BuildStageException exception = await Capture.ExceptionAsync<BuildStageException>(
            () => archiver.CreateArchiveAsync(source, archive, ArchiveFormats.Zip, CancellationToken.None));

        await Assert.That(exception.Message).Contains("does not exist");
        await Assert.That(exception.Message).Contains(archive.FullName);
    }

    [Test]
    public async Task Creating_an_archive_replaces_a_stale_one()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace, out _);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-win-x64.zip"));

        await archiver.CreateArchiveAsync(source, archive, ArchiveFormats.Zip, CancellationToken.None);

        // A previous run's stale archive must not survive into this run's output.
        foreach (string file in Directory.GetFiles(source.FullName, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        Directory.Delete(Path.Combine(source.FullName, "runtimes"), recursive: true);
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "replacement.txt"), "new content");
        await archiver.CreateArchiveAsync(source, archive, ArchiveFormats.Zip, CancellationToken.None);

        await using ZipArchive zip = await ZipFile.OpenReadAsync(archive.FullName);
        string[] entries = [.. zip.Entries.Select(static entry => entry.FullName)];

        await Assert.That(entries).IsEquivalentTo(["replacement.txt"]);
    }

    [Test]
    public async Task A_target_without_an_archive_key_archives_to_zip()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string archivePath = await RunBuildAsync(
            workspace,
            """{ "targets": [ { "project": "src/Alpha/Alpha.csproj", "kinds": ["executables"] } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "publish": { "sources": [] } }""");

        // Archiving is opt-out, not opt-in - the zip default is implicit.
        await Assert.That(File.Exists(archivePath)).IsTrue();

        await using ZipArchive zip = await ZipFile.OpenReadAsync(archivePath);
        await Assert.That(zip.Entries.Select(static entry => entry.FullName))
            .IsEquivalentTo(["publish-output.txt"]);
    }

    [Test]
    public async Task A_declared_tar_gz_target_archives_to_tar_gz()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string archivePath = await RunBuildAsync(
            workspace,
            """{ "targets": [ { "project": "src/Alpha/Alpha.csproj", "kinds": ["executables"], "archive": "tar.gz" } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "publish": { "sources": [] } }""");

        await Assert.That(archivePath.EndsWith(".tar.gz", StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(archivePath)).IsTrue();
    }

    [Test]
    public async Task A_declared_none_target_leaves_the_rid_folder_unarchived()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Alpha/Alpha.csproj", "kinds": ["executables"], "archive": "none" } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "publish": { "sources": [] } }""");
        invoker.OnInvocation = RecordingProcessInvoker.MaterializePublishOutput;

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(new BuildCommand(services), "build");

            await Assert.That(exitCode).IsEqualTo(0);

            // The per-rid folder still holds the publish output; only the archive is skipped.
            await Assert.That(File.Exists(
                Path.Combine(workspace.Root, "release", "win-x64", "Alpha", "publish-output.txt"))).IsTrue();
            await Assert.That(Directory.GetFiles(Path.Combine(workspace.Root, "release"), "*.zip"))
                .Count().IsEqualTo(0);
            await Assert.That(Directory.GetFiles(Path.Combine(workspace.Root, "release"), "*.tar.gz"))
                .Count().IsEqualTo(0);
        });
    }

    [Test]
    public async Task Package_kind_targets_never_get_an_archive()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(
            """{ "targets": [ { "project": "src/Alpha/Alpha.csproj", "kinds": ["lib-package"] } ], "build": { "runtimeIdentifiers": ["win-x64"] }, "publish": { "sources": [] } }""");
        invoker.OnInvocation = RecordingProcessInvoker.MaterializePublishOutput;

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(new BuildCommand(services), "build");

            await Assert.That(exitCode).IsEqualTo(0);

            // Package kinds produce no archives, even with the zip default in force.
            await Assert.That(Directory.GetFiles(Path.Combine(workspace.Root, "release"), "*.zip"))
                .Count().IsEqualTo(0);
            await Assert.That(Directory.GetFiles(Path.Combine(workspace.Root, "release"), "*.tar.gz"))
                .Count().IsEqualTo(0);
        });
    }

    /// <summary>
    /// Runs one fake-published build (a recording invoker that materializes each <c>-o</c>
    /// output folder) against the workspace's executables-kind manifest, and returns the single
    /// archive path the declared-or-defaulted format produced.
    /// </summary>
    /// <param name="workspace">The workspace carrying the manifest.</param>
    /// <param name="manifestJson">The manifest driving the build.</param>
    /// <returns>The full path of the created archive.</returns>
    private async Task<string> RunBuildAsync(TestWorkspace workspace, string manifestJson)
    {
        workspace.WriteManifest(manifestJson);
        invoker.OnInvocation = RecordingProcessInvoker.MaterializePublishOutput;

        string? archivePath = null;

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            int exitCode = await CliHarness.InvokeAsync(new BuildCommand(services), "build");

            await Assert.That(exitCode).IsEqualTo(0);

            string[] archives =
            [
                .. Directory.GetFiles(Path.Combine(workspace.Root, "release"), "*.zip"),
                .. Directory.GetFiles(Path.Combine(workspace.Root, "release"), "*.tar.gz")
            ];

            await Assert.That(archives.Length).IsEqualTo(1);
            archivePath = archives[0];
        });

        await Assert.That(archivePath).IsNotNull();

        return archivePath!;
    }

    private static DirectoryInfo SourceFolderWithContent(TestWorkspace workspace, out string[] expectedEntries)
    {
        DirectoryInfo source = new(workspace.CreateDirectory("release", "win-x64", "Alpha"));

        File.WriteAllText(Path.Combine(source.FullName, "Alpha.exe"), "binary payload");
        Directory.CreateDirectory(Path.Combine(source.FullName, "runtimes"));
        File.WriteAllText(Path.Combine(source.FullName, "runtimes", "native.dll"), "native payload");

        expectedEntries = ["Alpha.exe", "runtimes/native.dll"];
        return source;
    }
}