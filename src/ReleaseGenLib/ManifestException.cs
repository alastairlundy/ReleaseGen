namespace ReleaseGenLib;

/// <summary>
/// Thrown when a manifest fails parsing or validation. Messages name the failing field, target,
/// or searched directories, per the D009 error style.
/// </summary>
public class ManifestException(string message) : Exception(message);
