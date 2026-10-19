namespace ReleaseGenLib;

using System.Linq;
using System.Text.Json;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// Loads and validates <c>releasegen.json</c> manifests: strict System.Text.Json parsing (D002,
/// T003), walk-up discovery with an explicit-path override (D009), and the executable-kind
/// cross-check against the evaluated OutputType (D020).
/// </summary>
/// <param name="projectInfoProvider">
/// The Microsoft.Build evaluation path used for the D020 cross-check. When null, only
/// manifest-internal validation runs.
/// </param>
public sealed class ManifestLoader(IProjectInfoProvider? projectInfoProvider = null)
{
    /// <summary>
    /// The manifest file name resolved by discovery (T003).
    /// </summary>
    public const string ManifestFileName = "releasegen.json";

    /// <summary>
    /// Loads the manifest: an explicit path argument wins over discovery (D009).
    /// </summary>
    /// <param name="explicitManifestPath">
    /// The manifest path passed by the caller, or null to run discovery from the current directory.
    /// </param>
    /// <returns>The parsed and validated manifest.</returns>
    /// <exception cref="ManifestException">The manifest was not found, failed parse, or failed validation.</exception>
    public Manifest Load(FileInfo? explicitManifestPath)
    {
        return explicitManifestPath is not null
            ? LoadFromFile(explicitManifestPath)
            : LoadFromDirectory(new DirectoryInfo(Environment.CurrentDirectory));
    }

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> upward, stopping at the first manifest, and
    /// fails naming the searched directories when none is found (D009). The build stage uses this
    /// to learn the manifest's directory for resolving relative target project paths.
    /// </summary>
    /// <param name="startDirectory">The directory discovery starts from, typically the current directory.</param>
    /// <returns>The manifest file that was found.</returns>
    /// <exception cref="ManifestException">No manifest was found in the walk-up chain.</exception>
    public FileInfo FindManifestFile(DirectoryInfo startDirectory)
    {
        List<string> searchedDirectories = [];

        for (DirectoryInfo? directory = startDirectory; directory is not null; directory = directory.Parent)
        {
            searchedDirectories.Add(directory.FullName);

            FileInfo candidate = new(Path.Combine(directory.FullName, ManifestFileName));

            if (candidate.Exists)
            {
                return candidate;
            }
        }

        throw new ManifestException(
            $"No {ManifestFileName} found. Searched directories: {string.Join(", ", searchedDirectories)}");
    }

    /// <summary>
    /// Walks from <paramref name="startDirectory"/> upward, stopping at the first manifest, and
    /// fails naming the searched directories when none is found (D009).
    /// </summary>
    /// <param name="startDirectory">The directory discovery starts from, typically the current directory.</param>
    /// <returns>The parsed and validated manifest.</returns>
    /// <exception cref="ManifestException">No manifest was found, or it failed parse or validation.</exception>
    public Manifest LoadFromDirectory(DirectoryInfo startDirectory)
    {
        return LoadFromFile(FindManifestFile(startDirectory));
    }

    /// <summary>
    /// Parses and validates the manifest at the given path.
    /// </summary>
    /// <param name="manifestFile">The manifest file to load.</param>
    /// <returns>The parsed and validated manifest.</returns>
    /// <exception cref="ManifestException">The manifest failed parse or validation.</exception>
    public Manifest LoadFromFile(FileInfo manifestFile)
    {
        if (!manifestFile.Exists)
        {
            throw new ManifestException($"Manifest file not found: {manifestFile.FullName}");
        }

        Manifest manifest = Parse(manifestFile.OpenText().ReadToEnd());

        Validate(manifest, manifestFile);

        return manifest;
    }

    /// <summary>
    /// Parses manifest JSON strictly (D002, T003): an unknown field fails parse with an error
    /// naming the field.
    /// </summary>
    /// <param name="json">The manifest JSON text.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="ManifestException">The JSON was malformed or carried an unknown field.</exception>
    public Manifest Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.Manifest)
                   ?? throw new ManifestException("Manifest parse failed: the manifest is empty.");
        }
        catch (JsonException ex)
        {
            // The STJ message names the offending property for unmapped members (D002).
            throw new ManifestException($"Manifest parse failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a parsed manifest: every target names a project and only known artifact kinds,
    /// and a declared executables kind agrees with the evaluated OutputType (D020).
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="manifestFile">The manifest file, for resolving the targets' relative project paths.</param>
    /// <exception cref="ManifestException">A target entry or kind declaration is invalid, or the D020 cross-check fails.</exception>
    public void Validate(Manifest manifest, FileInfo manifestFile)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        string baseDirectory = manifestFile.DirectoryName ?? Environment.CurrentDirectory;

        foreach (ManifestTarget target in manifest.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Project))
            {
                throw new ManifestException(
                    $"Manifest validation failed: a target entry in {ManifestFileName} has an empty 'project' key.");
            }

            if (target.Kinds.Length == 0)
            {
                throw new ManifestException(
                    $"Manifest validation failed: the target '{target.Project}' declares no artifact kinds.");
            }

            foreach (string kind in target.Kinds)
            {
                if (!ArtifactKinds.IsKnownKind(kind))
                {
                    throw new ManifestException(
                        $"Manifest validation failed: the target '{target.Project}' declares unknown artifact kind '{kind}'. Known kinds: {string.Join(", ", ArtifactKinds.All)}.");
                }
            }

            if (target.Archive is not null && !ArchiveFormats.IsKnown(target.Archive))
            {
                throw new ManifestException(
                    $"Manifest validation failed: the target '{target.Project}' declares unknown archive format '{target.Archive}'. Known formats: {string.Join(", ", ArchiveFormats.All)}.");
            }

            if (projectInfoProvider is not null && target.Kinds.Contains(ArtifactKinds.Executables))
            {
                ValidateExecutablesOutputType(target, baseDirectory);
            }
        }
    }

    /// <summary>
    /// D020: a declared executables kind that disagrees with the evaluated OutputType fails
    /// manifest validation naming the target and the mismatch.
    /// </summary>
    /// <param name="target">The target declaring the executables kind.</param>
    /// <param name="baseDirectory">The manifest's directory, for resolving the project path.</param>
    private void ValidateExecutablesOutputType(ManifestTarget target, string baseDirectory)
    {
        FileInfo projectFile = ResolveProjectFile(target.Project, baseDirectory);

        string outputType;

        try
        {
            outputType = projectInfoProvider!.GetOutputType(projectFile);
        }
        catch (Exception ex)
        {
            throw new ManifestException(
                $"Manifest validation failed: could not evaluate '{projectFile.FullName}' to read its OutputType for the target '{target.Project}'. {ex.Message}");
        }

        bool outputTypeIsExecutable =
            string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase);

        if (!outputTypeIsExecutable)
        {
            throw new ManifestException(
                $"Manifest validation failed: the target '{target.Project}' declares the '{ArtifactKinds.Executables}' kind but MSBuild evaluation of '{projectFile.FullName}' reports OutputType '{outputType}' (expected 'Exe' or 'WinExe').");
        }
    }

    /// <summary>
    /// Resolves a target's project value to a project file. Relative paths resolve from the
    /// manifest's directory. Shared by manifest validation and the build stage (T009) so both
    /// stages follow one resolution rule (D009).
    /// </summary>
    /// <param name="projectPath">The target's 'project' value.</param>
    /// <param name="baseDirectory">The manifest's directory.</param>
    /// <returns>The resolved project file.</returns>
    /// <exception cref="ManifestException">The path names no project file or is ambiguous.</exception>
    public static FileInfo ResolveProjectFile(string projectPath, string baseDirectory)
    {
        string fullPath = Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.Combine(baseDirectory, projectPath);

        bool hasProjectExtension =
            fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

        if (hasProjectExtension)
        {
            return new FileInfo(fullPath);
        }

        List<FileInfo> projectFiles =
            [.. new DirectoryInfo(fullPath).EnumerateFiles("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            }).Where(f =>
                f.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))];

        return projectFiles.Count switch
        {
            1 => projectFiles[0],
            0 => throw new ManifestException(
                $"Manifest validation failed: no .NET project file found at '{projectPath}'."),
            _ => throw new ManifestException(
                $"Manifest validation failed: multiple project files found at '{projectPath}': {string.Join(", ", projectFiles.Select(f => f.Name))}. Name one project file in the target."),
        };
    }
}
