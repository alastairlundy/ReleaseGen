namespace ReleaseGenLib.Models;

/// <summary>
/// The release artifact kinds a manifest target can declare (D015).
/// </summary>
public static class ArtifactKinds
{
    /// <summary>
    /// Release binaries produced by the build stage.
    /// </summary>
    public const string Executables = "executables";

    /// <summary>
    /// A library NuGet package produced by the package stage.
    /// </summary>
    public const string LibPackage = "lib-package";

    /// <summary>
    /// A dotnet tool NuGet package produced by the package stage.
    /// </summary>
    public const string ToolPackage = "tool-package";

    /// <summary>
    /// The full set of kinds the v1 stages produce.
    /// </summary>
    public static readonly string[] All = [Executables, LibPackage, ToolPackage];

    /// <summary>
    /// Returns whether <paramref name="kind"/> is a kind the v1 stages produce.
    /// </summary>
    /// <param name="kind">The declared artifact kind.</param>
    public static bool IsKnownKind(string kind) =>
        kind is Executables or LibPackage or ToolPackage;
}
