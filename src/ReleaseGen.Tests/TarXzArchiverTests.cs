using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenLib.Exceptions;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// tar.xz creation: the format's compression is the system <c>xz</c> binary rather than a
/// managed stream, a machine without that binary fails with the environment-not-tooled error
/// naming the tool, and the staged tar never outlives a run. The leg that proves the archiver
/// spawns <c>xz</c> over a staged tar without needing one installed lives in
/// <see cref="ArchiveTests"/>; these tests compose from the production container via
/// <see cref="ReleaseGenRealInvokerDiAttribute"/> because their subject is a real process.
/// </summary>
[ReleaseGenRealInvokerDi]
public class TarXzArchiverTests
{
    private readonly IBuildArchiver _archiver;
    private readonly IProcessInvoker _processInvoker;

    /// <summary>
    /// tar.xz creation: the format compresses through the system <c>xz</c> binary, a missing
    /// binary fails naming the tool, and staging never outlives a run.
    /// </summary>
    public TarXzArchiverTests(ServiceProvider services)
    {
        _archiver = services.GetRequiredService<IBuildArchiver>();
        _processInvoker = services.GetRequiredService<IProcessInvoker>();
    }

    [Test]
    public async Task The_tar_xz_format_compresses_through_the_system_xz_binary()
    {
        Skip.Unless(
            await XzIsAvailableAsync().ConfigureAwait(false),
            "xz is not on PATH on this machine, so the success leg cannot run here.");

        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-linux-x64.tar.xz"));

        await _archiver.CreateArchiveAsync(source, archive, ArchiveFormats.TarXz, CancellationToken.None);

        await Assert.That(File.Exists(archive.FullName)).IsTrue();

        // The xz stream signature (FD 37 7A 58 5A 00): the compression came from xz, and the
        // staged .tar and .xz products were consumed - no staging file outlives the call.
        byte[] header = [.. File.ReadAllBytes(archive.FullName).Take(6)];
        await Assert.That(header).IsEquivalentTo(new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 });
        await Assert.That(Directory.GetFiles(workspace.Root, "*.tar").Length).IsEqualTo(0);
        await Assert.That(Directory.GetFiles(workspace.Root, "*.tar.xz.tar.xz").Length).IsEqualTo(0);
    }

    [Test]
    public async Task A_missing_xz_binary_fails_naming_the_tool_it_needs()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        DirectoryInfo source = SourceFolderWithContent(workspace);
        FileInfo archive = new(Path.Combine(workspace.Root, "Alpha-linux-x64.tar.xz"));

        // The binary name is injectable, so the environment-not-tooled path is deterministic
        // instead of depending on what this machine has installed.
        BuildArchiver archiver = new(_processInvoker, "releasegen-test-xz-not-on-path");

        BuildStageException exception = await Capture.ExceptionAsync<BuildStageException>(
            () => archiver.CreateArchiveAsync(source, archive, ArchiveFormats.TarXz, CancellationToken.None));

        // The error names the missing binary, where it was looked for, and the format it
        // would have produced.
        await Assert.That(exception.Message).Contains("releasegen-test-xz-not-on-path");
        await Assert.That(exception.Message).Contains("not found on PATH");
        await Assert.That(exception.Message).Contains(ArchiveFormats.TarXz);

        // Nothing half-written: no archive, and no staging tar left beside it.
        await Assert.That(File.Exists(archive.FullName)).IsFalse();
        await Assert.That(Directory.GetFiles(workspace.Root, "*.tar").Length).IsEqualTo(0);
    }

    /// <summary>
    /// Reports whether this machine has the <c>xz</c> binary on PATH, by asking it for its
    /// version; a spawn failure answers false, which is the skip reason for the success leg.
    /// </summary>
    /// <returns><see langword="true"/> when <c>xz</c> runs here.</returns>
    private async Task<bool> XzIsAvailableAsync()
    {
        try
        {
            BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(
                    new ProcessConfiguration("xz", ["--version"]),
                    null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            return result.ExitCode == 0;
        }
        catch (Exception)
        {
            // Not installed (or not runnable) here: the success leg skips instead of failing.
            return false;
        }
    }

    /// <summary>
    /// A small file tree to archive: one file at the root and one in a nested folder, so both
    /// entry shapes reach the compressed tar.
    /// </summary>
    /// <param name="workspace">The workspace staging the folder.</param>
    /// <returns>The source folder.</returns>
    private static DirectoryInfo SourceFolderWithContent(TestWorkspace workspace)
    {
        DirectoryInfo source = new(workspace.CreateDirectory("release", "linux-x64", "Alpha"));

        File.WriteAllText(Path.Combine(source.FullName, "Alpha"), "binary payload");
        Directory.CreateDirectory(Path.Combine(source.FullName, "runtimes"));
        File.WriteAllText(Path.Combine(source.FullName, "runtimes", "native.so"), "native payload");

        return source;
    }
}
