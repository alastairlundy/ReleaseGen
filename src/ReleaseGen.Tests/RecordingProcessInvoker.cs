namespace ReleaseGen.Tests;

using CliInvoke.Core;

/// <summary>
/// A fake <see cref="IProcessInvoker"/> that records every invocation instead of starting a
/// process, so tests can assert on the exact command lines the stages build
/// without network access or a real toolchain run. Publish integration tests, which push to a
/// local source, use the real invoker instead. Tests receive one per test through their DI
/// scope, so recorded invocations never leak across tests.
/// </summary>
public sealed class RecordingProcessInvoker : IProcessInvoker
{
    private readonly List<ProcessConfiguration> _invocations = [];

    /// <summary>
    /// Gets the recorded invocations, in call order.
    /// </summary>
    public IReadOnlyList<ProcessConfiguration> Invocations => _invocations;

    /// <summary>
    /// Gets or sets the exit code each recorded invocation returns; null always returns 0.
    /// Key off the recorded argument list to fail only the invocation under test.
    /// </summary>
    public Func<ProcessConfiguration, int>? ExitCodeFactory { get; set; }

    /// <summary>
    /// Gets or sets an optional per-invocation side effect, used to materialize the publish
    /// output folder a fake <c>dotnet publish</c> would have produced before the build stage
    /// archives it.
    /// </summary>
    public Action<ProcessConfiguration>? OnInvocation { get; set; }

    /// <inheritdoc />
    public Task<ProcessResult> ExecuteAsync(
        ProcessConfiguration configuration,
        ProcessExitConfiguration? exitConfiguration,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The suite records buffered invocations only.");

    /// <inheritdoc />
    public Task<BufferedProcessResult> ExecuteBufferedAsync(
        ProcessConfiguration configuration,
        ProcessExitConfiguration? exitConfiguration,
        CancellationToken cancellationToken)
    {
        _invocations.Add(configuration);
        OnInvocation?.Invoke(configuration);

        int exitCode = ExitCodeFactory?.Invoke(configuration) ?? 0;
        DateTime now = DateTime.UtcNow;

        BufferedProcessResult result = new(
            configuration.TargetFilePath,
            exitCode,
            Environment.ProcessId,
            string.Empty,
            string.Empty,
            now,
            now,
            canceled: false,
            signal: null,
            wasTruncated: false);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Renders one recorded invocation as <c>dotnet &lt;argument list&gt;</c> for assertions on
    /// the exact command line a stage built.
    /// </summary>
    /// <param name="configuration">The recorded invocation.</param>
    /// <returns>The rendered command line.</returns>
    public static string Render(ProcessConfiguration configuration) =>
        $"{Path.GetFileName(configuration.TargetFilePath)} {string.Join(' ', configuration.ArgumentList)}";

    /// <summary>
    /// A fake <c>dotnet publish</c> side effect: creates the <c>-o</c> output folder with one
    /// fixture file inside, so the build stage's archive step sees the folder a real
    /// publish would have produced.
    /// </summary>
    /// <param name="configuration">The recorded publish invocation.</param>
    public static void MaterializePublishOutput(ProcessConfiguration configuration)
    {
        IReadOnlyList<string> arguments = configuration.ArgumentList;

        for (int i = 0; i < arguments.Count - 1; i++)
        {
            if (!string.Equals(arguments[i], "-o", StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(arguments[i + 1]);
            File.WriteAllText(
                Path.Combine(arguments[i + 1], "publish-output.txt"), "fixture publish output.");
            return;
        }
    }
}
