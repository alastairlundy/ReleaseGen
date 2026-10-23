namespace ReleaseGenCli.Commands;

/// <summary>
/// Thrown when a pack invocation exits nonzero; the message names the target, the kind, and
/// the exit code, and the package stage command renders it as a one-line error.
/// </summary>
public sealed class PackageStageException(string message) : Exception(message);