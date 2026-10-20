namespace ReleaseGenLib.Models;

/// <summary>
/// How the test gate classified one test-stage run: the declared test section
/// passed, blocked, or was absent from the manifest.
/// </summary>
public enum TestGateOutcome
{
    /// <summary>
    /// The manifest declared a test section and <c>dotnet test</c> exited 0; the publish stage
    /// proceeds.
    /// </summary>
    Passed,

    /// <summary>
    /// The manifest declared a test section and <c>dotnet test</c> exited nonzero; the publish
    /// stage blocks, and a chained run aborts before publish.
    /// </summary>
    Blocked,

    /// <summary>
    /// The manifest declared no test section; the publish stage proceeds with a warning - a
    /// test-free manifest is the opt-in default, never a failure.
    /// </summary>
    NoSection,
}

/// <summary>
/// The test gate's recorded outcome for one stage invocation: the publish stage
/// consumes this instead of re-running tests. <see cref="ExitCode"/> carries the failing
/// <c>dotnet test</c> exit code only when <see cref="Outcome"/> is
/// <see cref="TestGateOutcome.Blocked"/>; passing and section-absent runs carry none.
/// </summary>
/// <param name="Outcome">How the gate rule classifies the run.</param>
/// <param name="ExitCode">The nonzero exit code that blocked publish, or null otherwise.</param>
public sealed record TestGateResult(TestGateOutcome Outcome, int? ExitCode)
{
    /// <summary>
    /// Maps a completed test-stage run to its gate outcome: a declared section with exit
    /// code 0 passes, a declared section with a nonzero exit blocks - and a failed stage aborts
    /// the chain before publish - while an absent section lands on the warning path
    /// rather than failing.
    /// </summary>
    /// <param name="exitCode">
    /// The <c>dotnet test</c> exit code; only meaningful when <paramref name="hasTestSection"/> is true.
    /// </param>
    /// <param name="hasTestSection">Whether the manifest declared a test section.</param>
    /// <returns>The recorded gate outcome.</returns>
    public static TestGateResult FromStage(int exitCode, bool hasTestSection) =>
        !hasTestSection
            ? new TestGateResult(TestGateOutcome.NoSection, null)
            : exitCode == 0
                ? new TestGateResult(TestGateOutcome.Passed, null)
                : new TestGateResult(TestGateOutcome.Blocked, exitCode);
}
