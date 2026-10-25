namespace ReleaseGen.Tests;

using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// A fixed project evaluation standing in for the Microsoft.Build path, so the manifest
/// loader's kind cross-checks stay off the real toolchain in contract tests. The shared
/// instance reports the permissive evaluation (Exe, PackAsTool true, IsPackable true) the
/// stage tests' executables- and tool-package-kind manifests need; the cross-check unit tests
/// build their own instances with a configured OutputType or PackAsTool and count reads, and
/// the init detection tests map one evaluation per project file name.
/// </summary>
internal sealed class StubProjectInfoProvider : IProjectInfoProvider
{
    private readonly ProjectEvaluation _default;
    private readonly IReadOnlyDictionary<string, ProjectEvaluation>? _evaluationsByFileName;

    /// <summary>
    /// A fixed project evaluation standing in for the Microsoft.Build path, so the manifest
    /// loader's kind cross-checks stay off the real toolchain in contract tests. The shared
    /// instance reports the permissive evaluation (Exe, PackAsTool true, IsPackable true) the
    /// stage tests' executables- and tool-package-kind manifests need; the cross-check unit
    /// tests build their own instances with a configured OutputType or PackAsTool and count
    /// reads.
    /// </summary>
    /// <param name="outputType">The OutputType the stub reports for every project.</param>
    /// <param name="packAsTool">The PackAsTool value the stub reports for every project.</param>
    /// <param name="isPackable">The IsPackable value the stub reports for every project.</param>
    public StubProjectInfoProvider(
        string outputType = "Exe",
        bool packAsTool = true,
        bool isPackable = true)
        : this(new ProjectEvaluation(outputType, Bool(packAsTool), Bool(isPackable)), null)
    {
    }

    /// <summary>
    /// A per-project-file stub for tests that classify several projects at once: each
    /// configured file name (for example 'App.csproj') answers with its own evaluation, and an
    /// unconfigured file fails the test's detection loudly instead of seeding a wrong answer.
    /// </summary>
    /// <param name="evaluations">The evaluation per project file name.</param>
    public StubProjectInfoProvider(IReadOnlyDictionary<string, ProjectEvaluation> evaluations)
        : this(new ProjectEvaluation("Library", string.Empty, string.Empty), evaluations)
    {
    }

    private StubProjectInfoProvider(
        ProjectEvaluation defaultEvaluation,
        IReadOnlyDictionary<string, ProjectEvaluation>? evaluationsByFileName)
    {
        _default = defaultEvaluation;
        _evaluationsByFileName = evaluationsByFileName;
    }

    /// <summary>
    /// Gets how often the kind cross-check ran one evaluation pass over the stub, for tests
    /// asserting a target's kinds are answered by a single pass and that a target declaring
    /// no cross-checked kind never consults the evaluation at all.
    /// </summary>
    public int EvaluationReads { get; private set; }

    /// <inheritdoc />
    public ProjectEvaluation EvaluateProject(FileInfo projectFile)
    {
        EvaluationReads++;

        if (_evaluationsByFileName is not null &&
            _evaluationsByFileName.TryGetValue(projectFile.Name, out ProjectEvaluation? evaluation))
        {
            return evaluation;
        }

        if (_evaluationsByFileName is not null)
        {
            throw new NotSupportedException(
                $"The stub has no configured evaluation for '{projectFile.Name}'.");
        }

        return _default;
    }

    /// <inheritdoc />
    public ProjectInfo DetectProjectInfo(FileInfo projectFile) =>
        throw new NotSupportedException("The manifest contract tests only read expanded properties.");

    /// <inheritdoc />
    public bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo)
    {
        projectInfo = null;
        return false;
    }

    private static string Bool(bool value) => value ? "true" : "false";
}
