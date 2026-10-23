namespace ReleaseGenLib.Exceptions;

/// <summary>
/// Thrown when the build stage fails: a publish command invocation exits nonzero, or a build-stage
/// precondition cannot be met. Messages name the failing target, rid, and exit code per
/// the error style; the CLI surfaces them via its top-level handler (exit code 1).
/// </summary>
public class BuildStageException(string message) : Exception(message);