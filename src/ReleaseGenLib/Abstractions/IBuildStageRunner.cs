namespace ReleaseGenLib.Abstractions;

/// <summary>
/// Runs the build stage: one <c>dotnet publish -c Release -r &lt;rid&gt;</c> invocation
/// per configured rid, per executables-kind target, into per-rid folders under the local output
/// root. Supersedes the legacy IProjectBuilder surface, whose publish-only flags on
/// <c>dotnet build</c> and inverted ReadyToRun check are retired rather than patched.
/// </summary>
public interface IBuildStageRunner
{
    /// <summary>
    /// Executes the build stage. Textual publish output is streamed line-by-line through the
    /// request's output callbacks (IL3000-family analyzer warnings pass through unfiltered);
    /// on the first failing rid a <see cref="BuildStageException"/> naming the target,
    /// rid, and exit code aborts the stage (fail-fast).
    /// </summary>
    /// <param name="request">The resolved build-stage inputs: manifest, preset, and output locations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BuildStageException">A publish invocation failed or a precondition was not met.</exception>
    Task RunAsync(BuildStageRequest request, CancellationToken cancellationToken);
}
