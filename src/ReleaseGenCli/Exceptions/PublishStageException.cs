namespace ReleaseGenCli.Exceptions;

/// <summary>
/// Thrown when the publish stage cannot push: a declared kind has no package output, or a push
/// invocation exits nonzero; the message names the failing thing, and the publish stage
/// command renders it as a one-line error.
/// </summary>
public sealed class PublishStageException(string message) : Exception(message);