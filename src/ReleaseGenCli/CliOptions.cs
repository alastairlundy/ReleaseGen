namespace ReleaseGenCli;

/// <summary>
/// Shared command-line options for the ReleaseGen CLI.
/// </summary>
public static class CliOptions
{
    /// <summary>
    /// The only verbosity switch in v1: suppresses stage notes and progress while
    /// warnings and errors stay visible.
    /// </summary>
    public static readonly Option<bool> Quiet = new("--quiet")
    {
        Description = "Suppress stage notes and progress. Warnings and errors stay visible.",
        Recursive = true,
    };
}