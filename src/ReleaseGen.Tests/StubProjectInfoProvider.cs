namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// A fixed OutputType source standing in for the Microsoft.Build evaluation path, so the
/// manifest loader's executables cross-check stays off the real toolchain in tests. The shared
/// instance reports "Exe", the stage tests' executables-kind manifests need; the cross-check
/// unit tests build their own instances with "Library" and count reads.
/// </summary>
/// <param name="outputType">The OutputType the stub reports.</param>
internal sealed class StubProjectInfoProvider(string outputType = "Exe") : IProjectInfoProvider
{
    /// <summary>
    /// Gets how often the OutputType cross-check read the stub, for tests asserting the
    /// cross-check keys on the declared executables kind only.
    /// </summary>
    public int OutputTypeReads { get; private set; }

    /// <inheritdoc />
    public string GetOutputType(FileInfo projectFile)
    {
        OutputTypeReads++;
        return outputType;
    }

    /// <inheritdoc />
    public ProjectInfo DetectProjectInfo(FileInfo projectFile) =>
        throw new NotSupportedException("The manifest contract tests only read OutputType.");

    /// <inheritdoc />
    public bool TryDetectProjectInfo(FileInfo projectFile, out ProjectInfo? projectInfo)
    {
        projectInfo = null;
        return false;
    }
}
