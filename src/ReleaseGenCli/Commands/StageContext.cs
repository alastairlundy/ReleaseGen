namespace ReleaseGenCli.Commands;

using ReleaseGenLib.Models;

/// <summary>
/// The shared stage context: the in-process handoff between stage commands invoked within one
/// process, which is how the chained run executes them (D010). Its v1 surface is the test gate
/// (D004): the chained run records the test stage's outcome here after that stage exits, and
/// the publish stage consults the recorded outcome instead of re-running tests inside the same
/// chain. Stage commands invoked as separate processes share nothing - each standalone
/// <c>publish</c> consults the gate itself by running the test stage, and the cross-process
/// story is the documented artifact-location handoff contract (D003, D018), not this state.
/// </summary>
public static class StageContext
{
    /// <summary>
    /// The test stage's recorded gate outcome for this process (D004, D010), or null when no
    /// test stage has run in this process yet.
    /// </summary>
    public static TestGateResult? TestGate { get; set; }

    /// <summary>
    /// Clears recorded stage state, so an invocation never consults a stale gate outcome.
    /// </summary>
    public static void Reset() => TestGate = null;
}
