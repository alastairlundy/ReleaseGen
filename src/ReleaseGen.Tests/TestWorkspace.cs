namespace ReleaseGen.Tests;

using ReleaseGenLib;

/// <summary>
/// A disposable per-test repository fixture: a unique temp directory that can also become the
/// process current directory for the duration of one command-level test.
/// </summary>
/// <remarks>
/// Command-level tests touch process-global state: the stage commands discover the manifest from
/// <see cref="Environment.CurrentDirectory"/>, init writes into it, and the gate, output, and
/// credential channels are process-wide too. TUnit runs tests in parallel by default, so every
/// command-level test runs through <see cref="InDirectoryAsync{T}"/>, which serializes them
/// behind one lock and restores the previous current directory in its finally block. Pure unit
/// tests (parsing, preset resolution, archives) never enter the lock and stay parallel.
/// </remarks>
internal sealed class TestWorkspace : IDisposable
{
    private static readonly SemaphoreSlim CommandLock = new(1, 1);

    private TestWorkspace(string root) => Root = root;

    /// <summary>
    /// Gets the workspace root directory under the system temp path.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Creates a unique, empty workspace directory.
    /// </summary>
    public static TestWorkspace Create()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "ReleaseGen.Tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        return new TestWorkspace(root);
    }

    /// <summary>
    /// Creates (and returns) a directory under the workspace root, creating ancestors as needed.
    /// </summary>
    /// <param name="segments">The relative path segments below the workspace root.</param>
    public string CreateDirectory(params string[] segments)
    {
        string fullPath = Path.Combine([Root, .. segments]);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    /// <summary>
    /// Writes a file under the workspace root, creating parent directories as needed.
    /// </summary>
    /// <param name="relativePath">The path relative to the workspace root.</param>
    /// <param name="contents">The file contents.</param>
    /// <returns>The full path of the written file.</returns>
    public string WriteFile(string relativePath, string contents)
    {
        string fullPath = Path.Combine(Root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    /// <summary>
    /// Writes the workspace's root <c>releasegen.json</c> (T003) with the given contents.
    /// </summary>
    /// <param name="contents">The manifest JSON.</param>
    /// <returns>The full path of the manifest.</returns>
    public string WriteManifest(string contents) =>
        WriteFile(ManifestLoader.ManifestFileName, contents);

    /// <summary>
    /// Runs <paramref name="action"/> with <paramref name="directory"/> as the process current
    /// directory, serialized against every other command-level test and restored afterwards.
    /// </summary>
    /// <param name="directory">The directory to use as the current directory.</param>
    /// <param name="action">The test body.</param>
    /// <returns>The test body's result.</returns>
    public static async Task<T> InDirectoryAsync<T>(string directory, Func<Task<T>> action)
    {
        await CommandLock.WaitAsync();

        string previous = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = directory;
            return await action();
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            CommandLock.Release();
        }
    }

    /// <inheritdoc cref="InDirectoryAsync{T}(string,{Func{Task{T}}})" />
    public static Task InDirectoryAsync(string directory, Func<Task> action) =>
        InDirectoryAsync(directory, async () =>
        {
            await action();
            return true;
        });

    /// <summary>
    /// Deletes the workspace directory; failures to clean up never fail the test.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
