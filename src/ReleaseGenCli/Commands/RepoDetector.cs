namespace ReleaseGen.Cli.Commands;

using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The repo detection behind init's default path: it enumerates the .NET projects under one
/// root, classifies each through a single expanded-property evaluation, and keeps only what is
/// certain - executable targets (trusted by their OutputType), test projects matching the
/// <c>.Tests</c>/<c>.Test</c> naming convention, and package kinds from project metadata
/// (including an expanded <c>PackAsTool=true</c>) - while recording every declared target
/// framework for the interactive questionnaire's rid defaults. Every uncertain classification
/// contributes exactly one warning and seeds nothing, and an outright detection failure clears
/// everything it found so init falls back to the minimal skeleton.
/// </summary>
internal sealed class RepoDetector
{
    private readonly IProjectInfoProvider _projectInfoProvider;

    /// <summary>
    /// The repo detection behind init's default path: it enumerates the .NET projects under one
    /// root, classifies each through a single expanded-property evaluation, and keeps only what
    /// is certain.
    /// </summary>
    /// <param name="projectInfoProvider">The Microsoft.Build evaluation path classification reads.</param>
    public RepoDetector(IProjectInfoProvider projectInfoProvider) =>
        _projectInfoProvider = projectInfoProvider;

    /// <summary>
    /// Classifies every project file under <paramref name="root"/>. The call never throws: a
    /// project whose evaluation fails warns and seeds nothing, and a failure that overtakes the
    /// whole pass clears what was found, so the caller always gets a usable (possibly empty)
    /// detection.
    /// </summary>
    /// <param name="root">The directory to scan - init passes the current directory.</param>
    /// <returns>The seeded targets, test projects, and the warnings collected on the way.</returns>
    public RepoDetection Detect(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        RepoDetection detection = new();

        try
        {
            List<FileInfo> projectFiles = [.. EnumerateProjectFiles(root)];

            // Ordinal by full path: the seeded manifest reads the same on every machine
            // regardless of filesystem enumeration order.
            projectFiles.Sort(static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));

            foreach (FileInfo projectFile in projectFiles)
            {
                Classify(detection, root, projectFile);
            }
        }
        catch (Exception ex)
        {
            // Ambiguity overtakes the whole pass: seed nothing rather than half a manifest.
            detection.Clear();
            detection.AddWarning(
                $"init: repo detection failed ({ex.Message}); seeded nothing, so the minimal skeleton is written.");
        }

        return detection;
    }

    /// <summary>
    /// Classifies one project file: a test-name match seeds a test project, an executable
    /// OutputType seeds an executables target (with <c>tool-package</c> and the explicit archive
    /// default set where the metadata says so), and a library falls to its package metadata.
    /// Anything else warns once and seeds nothing.
    /// </summary>
    /// <param name="detection">The detection collecting seeds and warnings.</param>
    /// <param name="root">The scanned root, for the seeded relative project path.</param>
    /// <param name="projectFile">The project file to classify.</param>
    private void Classify(RepoDetection detection, DirectoryInfo root, FileInfo projectFile)
    {
        string relativePath = RelativePath(root, projectFile);

        ProjectEvaluation evaluation;

        try
        {
            evaluation = _projectInfoProvider.EvaluateProject(projectFile);
        }
        catch (Exception ex)
        {
            detection.AddWarning(
                $"init: could not evaluate '{relativePath}' ({ex.Message}); seeded nothing for it.");
            return;
        }

        // The declared TFMs of every project that evaluated cleanly feed the interactive
        // rid defaults (the osx-x64 union rule's TFM clause), whether or not the project
        // itself ends up seeded as a target.
        foreach (string targetFramework in evaluation.TargetFrameworks)
        {
            detection.AddTargetFramework(targetFramework);
        }

        if (IsTestProjectName(projectFile))
        {
            if (evaluation.DeclaresPackIntent)
            {
                detection.AddWarning(
                    $"init: '{relativePath}' matches the test project naming convention but also declares package metadata (PackAsTool or IsPackable); ambiguous, seeded nothing for it.");
                return;
            }

            detection.AddTestProject(relativePath);
            return;
        }

        if (evaluation.IsExecutableOutput)
        {
            // Trusted classification: the OutputType says executable, the metadata adds the
            // tool-package kind, and the explicit archive list makes D009's default set
            // visible in the file instead of hiding behind an absent key.
            detection.AddTarget(new ManifestTarget
            {
                Project = relativePath,
                Kinds = evaluation.PackAsToolIsTrue
                    ? [ArtifactKinds.Executables, ArtifactKinds.ToolPackage]
                    : [ArtifactKinds.Executables],
                ArchiveFormats = [ArchiveFormats.Zip, ArchiveFormats.TarGz],
            });
            return;
        }

        SeedLibraryTarget(detection, relativePath, evaluation);
    }

    /// <summary>
    /// Seeds the package kind a library-shaped project's metadata declares: <c>tool-package</c>
    /// for an expanded <c>PackAsTool=true</c>, <c>lib-package</c> for a packable project (the
    /// SDK's default), and exactly one warning seeding nothing when neither applies.
    /// </summary>
    /// <param name="detection">The detection collecting seeds and warnings.</param>
    /// <param name="relativePath">The project's relative path, for the seed and the warning.</param>
    /// <param name="evaluation">The project's expanded properties.</param>
    private static void SeedLibraryTarget(
        RepoDetection detection,
        string relativePath,
        ProjectEvaluation evaluation)
    {
        if (evaluation.PackAsToolIsTrue)
        {
            detection.AddTarget(new ManifestTarget
            {
                Project = relativePath,
                Kinds = [ArtifactKinds.ToolPackage],
            });
            return;
        }

        if (evaluation.IsPackableEnabled)
        {
            detection.AddTarget(new ManifestTarget
            {
                Project = relativePath,
                Kinds = [ArtifactKinds.LibPackage],
            });
            return;
        }

        detection.AddWarning(
            $"init: '{relativePath}' is not an executable, not packable, and not a test project; no release artifact kind applies, seeded nothing for it.");
    }

    /// <summary>
    /// Enumerates every .NET project file under <paramref name="root"/>, skipping build output
    /// (<c>bin</c>, <c>obj</c>) and dot-directories such as <c>.git</c>, with inaccessible
    /// directories ignored instead of failing the scan.
    /// </summary>
    /// <param name="root">The directory to scan.</param>
    /// <returns>The project files found, in filesystem order; the caller sorts them.</returns>
    private static IEnumerable<FileInfo> EnumerateProjectFiles(DirectoryInfo root)
    {
        Stack<DirectoryInfo> pending = new();
        pending.Push(root);

        EnumerationOptions fileOptions = new()
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = false,
        };

        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();

            foreach (FileInfo file in directory.EnumerateFiles("*", fileOptions))
            {
                if (file.Name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            foreach (DirectoryInfo child in directory.EnumerateDirectories())
            {
                if (IsSkippedDirectory(child))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    /// <summary>
    /// Whether a directory never holds sources worth classifying: build output, test results,
    /// and dot-directories (version control, IDE state).
    /// </summary>
    /// <param name="directory">The directory under consideration.</param>
    /// <returns><see langword="true"/> when the directory is skipped.</returns>
    private static bool IsSkippedDirectory(DirectoryInfo directory) =>
        directory.Name.StartsWith(".", StringComparison.Ordinal) ||
        string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(directory.Name, "obj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a project file name follows the test project naming convention: the file name
    /// without its extension ends in <c>.Tests</c> or <c>.Test</c>.
    /// </summary>
    /// <param name="projectFile">The project file.</param>
    /// <returns><see langword="true"/> when the name matches the convention.</returns>
    private static bool IsTestProjectName(FileInfo projectFile)
    {
        string name = Path.GetFileNameWithoutExtension(projectFile.Name);
        return name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The seeded project value: the path relative to the scanned root, in forward slashes so
    /// the manifest stays portable between machines and operating systems.
    /// </summary>
    /// <param name="root">The scanned root.</param>
    /// <param name="projectFile">The project file.</param>
    /// <returns>The relative, forward-slashed project path.</returns>
    private static string RelativePath(DirectoryInfo root, FileInfo projectFile) =>
        Path.GetRelativePath(root.FullName, projectFile.FullName).Replace('\\', '/');
}
