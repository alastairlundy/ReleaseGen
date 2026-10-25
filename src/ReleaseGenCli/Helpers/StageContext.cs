namespace ReleaseGenCli.Helpers;

/// <summary>
/// The shared stage context: the in-process handoff between stage commands invoked within one
/// process, which is how the chained run executes them. Its v1 surface is the test gate:
/// the chained run records the test stage's outcome here after that stage exits, and
/// the publish stage consults the recorded outcome instead of re-running tests inside the same
/// chain. Stage commands invoked as separate processes share nothing - each standalone
/// <c>publish</c> consults the gate itself by running the test stage, and the cross-process
/// story is the documented artifact-location handoff contract, not this state.
/// </summary>
public static class StageContext
{
    /// <summary>
    /// The test stage's recorded gate outcome for this process, or null when no
    /// test stage has run in this process yet.
    /// </summary>
    public static TestGateResult? TestGate { get; set; }

    /// <summary>
    /// Clears recorded stage state, so an invocation never consults a stale gate outcome.
    /// </summary>
    public static void Reset() => TestGate = null;
}
