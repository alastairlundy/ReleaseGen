namespace ReleaseGen.Cli.Commands;

using ReleaseGenLib.Models;

/// <summary>
/// What init's repo detection found: the targets and test projects safe to seed, plus one
/// warning per uncertain classification (which seeded nothing). An empty detection is not a
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
    /// Records one warning: one uncertain classification contributes exactly one entry, and
    /// nothing is seeded for it.
    /// </summary>
    /// <param name="warning">The warning text init prints before writing.</param>
    public void AddWarning(string warning) => Warnings.Add(warning);

    /// <summary>
    /// Drops every seed while keeping the warnings: the fallback path where detection failed or
    /// turned ambiguous, so init writes today's minimal skeleton.
    /// </summary>
    public void Clear()
    {
        Targets.Clear();
        TestProjects.Clear();
    }
}
