namespace ReleaseGen.Cli.Commands;

using ReleaseGenLib.Models;

/// <summary>
/// What init's repo detection found: the targets and test projects safe to seed, the declared
/// target frameworks the interactive rid defaults read, plus one warning per uncertain
/// classification (which seeded nothing). An empty detection is not a
/// failure state - it is the minimal skeleton shape init falls back to.
/// </summary>
internal sealed class RepoDetection
{
    /// <summary>
    /// Gets the detected targets: executable projects and projects whose metadata declares a
    /// package kind.
    /// </summary>
    public List<ManifestTarget> Targets { get; } = [];

    /// <summary>
    /// Gets the detected test projects: paths matching the <c>.Tests</c>/<c>.Test</c> naming
    /// convention, seeded into the manifest's test section.
    /// </summary>
    public List<string> TestProjects { get; } = [];

    /// <summary>
    /// Gets the declared target frameworks of every project evaluation detection ran -
    /// distinct, in first-seen order. The interactive questionnaire reads them for the
    /// osx-x64 default rule's TFM clause; a detection that failed and was cleared carries
    /// none, so the rule falls back to the installed-SDK clause alone.
    /// </summary>
    public List<string> TargetFrameworks { get; } = [];

    /// <summary>
    /// Gets the warnings collected during detection - one per project that could not be
    /// classified, or per the detection pass itself failing.
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Adds a target to the seeded set.
    /// </summary>
    /// <param name="target">The detected target.</param>
    public void AddTarget(ManifestTarget target) => Targets.Add(target);

    /// <summary>
    /// Adds a test project to the seeded set.
    /// </summary>
    /// <param name="projectPath">The test project's relative path.</param>
    public void AddTestProject(string projectPath) => TestProjects.Add(projectPath);

    /// <summary>
    /// Records one declared target framework, keeping the collection distinct so a repo
    /// that multi-targets the same TFM across projects still contributes one entry.
    /// </summary>
    /// <param name="targetFramework">One declared TFM, for example <c>net10.0-macos.15</c>.</param>
    public void AddTargetFramework(string targetFramework)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework) &&
            !TargetFrameworks.Contains(targetFramework, StringComparer.Ordinal))
        {
            TargetFrameworks.Add(targetFramework);
        }
    }

    /// <summary>
    /// Records one warning: one uncertain classification contributes exactly one entry, and
    /// nothing is seeded for it.
    /// </summary>
    /// <param name="warning">The warning text init prints before writing.</param>
    public void AddWarning(string warning) => Warnings.Add(warning);

    /// <summary>
    /// Drops every seed while keeping the warnings: the fallback path where detection failed or
    /// turned ambiguous, so init writes today's minimal skeleton. The declared TFMs go too -
    /// what could not be trusted to seed a target cannot be trusted to steer the rid defaults.
    /// </summary>
    public void Clear()
    {
        Targets.Clear();
        TestProjects.Clear();
        TargetFrameworks.Clear();
    }
}
