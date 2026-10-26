namespace ReleaseGen.Cli.Commands;

using ReleaseGenLib.Models;

/// <summary>
/// The five answers init's <c>--interactive</c> questionnaire collected, shaped into the
/// manifest content the write path composes: the chosen preset, the chosen targets with the
/// chosen archive formats stamped on their executables-kind entries, the chosen rids, and the
/// chosen test projects (empty here means no test section, keeping the gate's warn-and-pass
/// default).
/// </summary>
/// <param name="Preset">The chosen build preset, or null when preset none was chosen.</param>
/// <param name="Targets">The chosen targets, in prompt order, each carrying its archive formats.</param>
/// <param name="RuntimeIdentifiers">The chosen rids, in answer order; empty when none apply.</param>
/// <param name="TestProjects">The chosen test projects, in prompt order.</param>
internal sealed record InitAnswers(
    string? Preset,
    List<ManifestTarget> Targets,
    string[] RuntimeIdentifiers,
    List<string> TestProjects);
