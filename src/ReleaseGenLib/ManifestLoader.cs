using ReleaseGenLib.Exceptions;

namespace ReleaseGenLib;

using System.Linq;
using System.Text.Json;
using Abstractions;
using Models;

/// <summary>
/// Loads and validates <c>releasegen.json</c> manifests: strict System.Text.Json parsing,
/// walk-up discovery with an explicit-path override, and the kind cross-checks against one
/// expanded MSBuild evaluation (OutputType for <c>executables</c>, PackAsTool for
/// <c>tool-package</c>).
/// </summary>
public sealed class ManifestLoader : IManifestLoader
{
    private readonly IProjectInfoProvider _projectInfoProvider;

    /// <summary>
    /// Loads and validates <c>releasegen.json</c> manifests: strict System.Text.Json parsing,
    /// walk-up discovery with an explicit-path override, and the kind cross-checks against one
    /// expanded MSBuild evaluation (OutputType for <c>executables</c>, PackAsTool for
    /// <c>tool-package</c>).
    /// </summary>
    /// <param name="projectInfoProvider">
    /// The Microsoft.Build evaluation path behind the kind cross-checks. When null, only
    /// manifest-internal validation runs.
    /// </param>
    public ManifestLoader(IProjectInfoProvider projectInfoProvider)
    {
        _projectInfoProvider = projectInfoProvider;
    }

    /// <summary>
    /// The manifest file name resolved by discovery.
    /// </summary>
    public const string ManifestFileName = "releasegen.json";

    /// <summary>
    /// Loads the manifest: an explicit path argument wins over discovery.
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
    /// fails naming the searched directories when none is found. The build stage uses this
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
    /// fails naming the searched directories when none is found.
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

        using StreamReader reader = manifestFile.OpenText();
        Manifest manifest = Parse(reader.ReadToEnd());

        Validate(manifest, manifestFile);

        return manifest;
    }

    /// <summary>
    /// Parses manifest JSON strictly: an unknown field fails parse with an error
    /// naming the field.
    /// </summary>
    /// <param name="json">The manifest JSON text.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="ManifestException">The JSON was malformed or carried an unknown field.</exception>
    public Manifest Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, Contexts.ManifestJsonContext.Default.Manifest)
                   ?? throw new ManifestException("Manifest parse failed: the manifest is empty.");
        }
        catch (JsonException ex)
        {
            // The STJ message names the offending property for unmapped members.
            throw new ManifestException($"Manifest parse failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a parsed manifest: every target names a project and only known artifact kinds,
    /// a declared tar.xz format still leaves a usable format for every declared Windows-class or
    /// portable rid, a declared executables kind agrees with the evaluated OutputType, and a
    /// declared tool-package kind agrees with the expanded PackAsTool property.
    /// </summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="manifestFile">The manifest file, for resolving the targets' relative project paths.</param>
    /// <exception cref="ManifestException">A target entry or kind declaration is invalid, the tar.xz declaration conflicts with the declared rids, or a kind cross-check fails.</exception>
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

            foreach (string archiveFormat in target.ArchiveFormats)
            {
                if (!ArchiveFormats.IsKnown(archiveFormat))
                {
                    throw new ManifestException(
                        $"Manifest validation failed: the target '{target.Project}' declares unknown archive format '{archiveFormat}'. Known formats: {string.Join(", ", ArchiveFormats.All)}.");
                }
            }

            ValidateTarXzAgainstWindowsRids(manifest, target);

            bool declaresExecutables =
                target.Kinds.Contains(ArtifactKinds.Executables, StringComparer.OrdinalIgnoreCase);
            bool declaresToolPackage =
                target.Kinds.Contains(ArtifactKinds.ToolPackage, StringComparer.OrdinalIgnoreCase);

            if (_projectInfoProvider is not null && (declaresExecutables || declaresToolPackage))
            {
                // One evaluation pass answers both kind cross-checks, so a target declaring
                // both kinds evaluates its project once.
                ValidateProjectEvaluation(target, baseDirectory, declaresExecutables, declaresToolPackage);
            }
        }
    }

    /// <summary>
    /// A target declaring tar.xz as its only archive format while the manifest declares
    /// Windows-class or portable <c>any-*</c> runtime identifiers fails validation: those rids
    /// never produce tar.xz, so the target would ship no archive there. A target declaring
    /// another format alongside tar.xz validates instead: the build stage skips tar.xz, with
    /// one warning per unsupported rid.
    /// </summary>
    /// <param name="manifest">The parsed manifest, holding the declared runtime identifiers.</param>
    /// <param name="target">The target whose declared formats are checked.</param>
    /// <exception cref="ManifestException">tar.xz is the target's only declared format against an unsupported rid.</exception>
    private static void ValidateTarXzAgainstWindowsRids(Manifest manifest, ManifestTarget target)
    {
        if (!target.ArchiveFormats.Contains(ArchiveFormats.TarXz, StringComparer.Ordinal) ||
            !target.ArchiveFormats.All(format => string.Equals(format, ArchiveFormats.TarXz, StringComparison.Ordinal)))
        {
            // Absent keys never resolve to tar.xz, and any second declared format still
            // archives every rid, so no conflict can arise.
            return;
        }

        string[] unsupportedRids =
        [
            .. manifest.Build.RuntimeIdentifiers.Where(rid => !ArchiveFormats.SupportsTarXz(rid)),
        ];

        if (unsupportedRids.Length == 0)
        {
            return;
        }

        // The error style: names the target, the conflicting rids, and the fix.
        throw new ManifestException(
            $"Manifest validation failed: the target '{target.Project}' declares 'tar.xz' as its only archive format, but tar.xz cannot be produced for the runtime identifier(s) {string.Join(", ", unsupportedRids)}, so those rids would produce no archive. Fix: declare another archive format (for example '{ArchiveFormats.Zip}') in 'archiveFormats', or remove those runtime identifiers from build.runtimeIdentifiers.");
    }

    /// <summary>
    /// One MSBuild evaluation pass answers the declared kinds' project cross-checks: a
    /// declared executables kind must agree with the evaluated OutputType, and a declared
    /// tool-package kind must agree with the expanded PackAsTool property. Both read expanded
    /// properties - a Directory.Build.props-driven setup stays honored - and neither emits a
    /// build switch, so the project file stays authoritative.
    /// </summary>
    /// <param name="target">The target declaring the kinds.</param>
    /// <param name="baseDirectory">The manifest's directory, for resolving the project path.</param>
    /// <param name="declaresExecutables">Whether the target declares the executables kind.</param>
    /// <param name="declaresToolPackage">Whether the target declares the tool-package kind.</param>
    /// <exception cref="ManifestException">The evaluation failed, or a declared kind disagrees with it.</exception>
    private void ValidateProjectEvaluation(
        ManifestTarget target,
        string baseDirectory,
        bool declaresExecutables,
        bool declaresToolPackage)
    {
        FileInfo projectFile = ResolveProjectFile(target.Project, baseDirectory);

        ProjectEvaluation evaluation;

        try
        {
            evaluation = _projectInfoProvider.EvaluateProject(projectFile);
        }
        catch (Exception ex)
        {
            throw new ManifestException(
                $"Manifest validation failed: could not evaluate '{projectFile.FullName}' to read its expanded properties for the target '{target.Project}'. {ex.Message}");
        }

        if (declaresExecutables && !evaluation.IsExecutableOutput)
        {
            throw new ManifestException(
                $"Manifest validation failed: the target '{target.Project}' declares the '{ArtifactKinds.Executables}' kind but MSBuild evaluation of '{projectFile.FullName}' reports OutputType '{evaluation.OutputType}' (expected 'Exe' or 'WinExe').");
        }

        if (declaresToolPackage && !evaluation.PackAsToolIsTrue)
        {
            // The fix names the declaration site generically: PackAsTool most often lives in
            // an imported props file rather than the project file the target names.
            throw new ManifestException(
                $"Manifest validation failed: the target '{target.Project}' declares the '{ArtifactKinds.ToolPackage}' kind but MSBuild evaluation of '{projectFile.FullName}' reports PackAsTool '{ProjectEvaluation.Display(evaluation.PackAsTool)}' (expected 'true'). Fix: set PackAsTool to true where the project's MSBuild properties are declared - the project file or an imported props file such as Directory.Build.props. ReleaseGen reads the expanded property and never passes the switch itself.");
        }
    }

    /// <summary>
    /// Resolves a target's project value to a project file. Relative paths resolve from the
    /// manifest's directory. Shared by manifest validation and the build stage so both
    /// stages follow one resolution rule.
    /// </summary>
    /// <param name="projectPath">The target's 'project' value.</param>
    /// <param name="baseDirectory">The manifest's directory.</param>
    /// <returns>The resolved project file.</returns>
    /// <exception cref="ManifestException">The path names no project file or is ambiguous.</exception>
    public FileInfo ResolveProjectFile(string projectPath, string baseDirectory)
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

        List<FileInfo> projectFiles;

        try
        {
            projectFiles =
            [.. new DirectoryInfo(fullPath).EnumerateFiles("*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            }).Where(f =>
                f.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            throw new ManifestException(
                $"Manifest validation failed: no .NET project file found at '{projectPath}'. {ex.Message}");
        }

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
