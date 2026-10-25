namespace ReleaseGenLib.Models;

/// <summary>
/// The expanded MSBuild properties ReleaseGen reads in one evaluation pass over a project
/// file: <c>OutputType</c>, <c>PackAsTool</c>, and <c>IsPackable</c>. Every value is MSBuild's
/// expanded property, so a <c>Directory.Build.props</c> or any other imported props file flows
/// through unchanged - ReleaseGen never reads raw project text, and it never passes the
/// properties as build switches either: the project file stays authoritative.
/// </summary>
/// <param name="OutputType">
/// The expanded <c>OutputType</c>, defaulting to 'Library' the way MSBuild does when the
/// project sets none.
/// </param>
/// <param name="PackAsTool">The expanded <c>PackAsTool</c> property; empty when unset.</param>
/// <param name="IsPackable">The expanded <c>IsPackable</c> property; empty when unset.</param>
public sealed record ProjectEvaluation(string OutputType, string PackAsTool, string IsPackable)
{
    /// <summary>
    /// Whether the evaluated <c>OutputType</c> names an executable output: 'Exe' or 'WinExe'.
    /// </summary>
    public bool IsExecutableOutput =>
        string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the expanded <c>PackAsTool</c> property is <c>true</c> - the whole condition a
    /// <c>tool-package</c> target validates against.
    /// </summary>
    public bool PackAsToolIsTrue => IsTrue(PackAsTool);

    /// <summary>
    /// Whether the project is packable: a <c>PackAsTool</c> project always is, an explicitly
    /// false <c>IsPackable</c> never is, and an unset <c>IsPackable</c> follows the SDK's
    /// default of true (the pack targets default it when nothing sets it).
    /// </summary>
    public bool IsPackableEnabled =>
        PackAsToolIsTrue || string.IsNullOrEmpty(IsPackable) || IsTrue(IsPackable);

    /// <summary>
    /// Whether the project declares pack intent itself: <c>PackAsTool</c> true, or
    /// <c>IsPackable</c> explicitly true. An unset <c>IsPackable</c> is the SDK's default
    /// rather than a declaration, so it does not count as intent here.
    /// </summary>
    public bool DeclaresPackIntent =>
        PackAsToolIsTrue ||
        string.Equals(IsPackable, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The property as it should read in an error message: '<c>(unset)</c>' when the project
    /// expanded it to nothing, so a missing declaration is distinguishable from a false one.
    /// </summary>
    /// <param name="value">The expanded property value.</param>
    /// <returns>The value, or '(unset)' when empty.</returns>
    public static string Display(string value) =>
        string.IsNullOrEmpty(value) ? "(unset)" : value;

    private static bool IsTrue(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
