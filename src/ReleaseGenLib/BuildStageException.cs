namespace ReleaseGenLib;

/// <summary>
/// Thrown when the build stage fails: a publish invocation exits nonzero, or a build-stage
/// precondition cannot be met. Messages name the failing target, rid, and exit code per the
/// D009 error style; the CLI surfaces them via its top-level handler (exit code 1).
/// </summary>
public class BuildStageException(string message) : Exception(message);
