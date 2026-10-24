using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib.Abstractions;

public interface IManifestLoader
{
    /// <summary>
    /// Loads the manifest: an explicit path argument wins over discovery.
    /// </summary>
    /// <param name="explicitManifestPath">
    /// The manifest path passed by the caller, or null to run discovery from the current directory.
    /// </param>
    /// <returns>The parsed and validated manifest.</returns>
    /// <exception cref="ManifestException">The manifest was not found, failed parse, or failed validation.</exception>
    Manifest Load(FileInfo? explicitManifestPath);

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> upward, stopping at the first manifest, and
    /// fails naming the searched directories when none is found. The build stage uses this
    /// to learn the manifest's directory for resolving relative target project paths.
    /// </summary>
    /// <param name="startDirectory">The directory discovery starts from, typically the current directory.</param>
    /// <returns>The manifest file that was found.</returns>
    /// <exception cref="ManifestException">No manifest was found in the walk-up chain.</exception>
    FileInfo FindManifestFile(DirectoryInfo startDirectory);

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> upward, stopping at the first manifest, and
    /// fails naming the searched directories when none is found.
    /// </summary>
    /// <param name="startDirectory">The directory discovery starts from, typically the current directory.</param>
    /// <returns>The parsed and validated manifest.</returns>
    /// <exception cref="ManifestException">No manifest was found, or it failed parse or validation.</exception>
    Manifest LoadFromDirectory(DirectoryInfo startDirectory);

    /// <summary>
    /// Parses and validates the manifest at the given path.
    /// </summary>
    /// <param name="manifestFile">The manifest file to load.</param>
    /// <returns>The parsed and validated manifest.</returns>
    /// <exception cref="ManifestException">The manifest failed parse or validation.</exception>
    Manifest LoadFromFile(FileInfo manifestFile);

    /// <summary>
    /// Parses manifest JSON strictly: an unknown field fails parse with an error
    /// naming the field.
    /// </summary>
    /// <param name="json">The manifest JSON text.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="ManifestException">The JSON was malformed or carried an unknown field.</exception>
    Manifest Parse(string json);

    /// <summary>
    /// Validates a parsed manifest: every target names a project and only known artifact kinds,
    /// and a declared executables kind agrees with the evaluated OutputType.
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="manifestFile">The manifest file, for resolving the targets' relative project paths.</param>
    /// <exception cref="ManifestException">A target entry or kind declaration is invalid, or the OutputType cross-check fails.</exception>
    void Validate(Manifest manifest, FileInfo manifestFile);

    /// <summary>
    /// Resolves a target's project value to a project file. Relative paths resolve from the
    /// manifest's directory. Shared by manifest validation and the build stage so both
    /// stages follow one resolution rule.
    /// </summary>
    /// <param name="projectPath">The target's 'project' value.</param>
    /// <param name="baseDirectory">The manifest's directory.</param>
    /// <returns>The resolved project file.</returns>
    /// <exception cref="ManifestException">The path names no project file or is ambiguous.</exception>
    FileInfo ResolveProjectFile(string projectPath, string baseDirectory);
}