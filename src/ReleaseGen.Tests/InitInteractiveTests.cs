using ReleaseGen.Cli.Commands;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;
using ReleaseGenLib.Models;

/// <summary>
/// The init questionnaire behind <c>--interactive</c>: the five questions arrive in their
/// fixed order, Enter accepts each detected default, typed answers are validated against the
/// manifest's own sets, executable targets refuse to leave without a rid, input ending
/// mid-question writes nothing, and a non-tty stdin fails with an error that names the flag
/// and points at plain init. The prompts ride an injected writer (the capture console never
/// sees them) while errors and warnings ride the captured console, and every run that can
/// write goes through <see cref="TestWorkspace.InDirectoryAsync"/> per AGENTS.md.
/// </summary>
[ReleaseGenDi]
public class InitInteractiveTests
{
    private readonly IManifestLoader _loader;

    /// <summary>
    /// The init questionnaire behind <c>--interactive</c>: the five questions arrive in their
    /// fixed order, Enter accepts each detected default, typed answers are validated against
    /// the manifest's own sets, executable targets refuse to leave without a rid, input ending
    /// mid-question writes nothing, and a non-tty stdin fails with an error that names the flag
    /// and points at plain init.
    /// </summary>
    /// <param name="loader">The strict manifest parser the written file is verified against.</param>
    public InitInteractiveTests(IManifestLoader loader) => _loader = loader;

    [Test]
    public async Task The_five_questions_arrive_in_order_and_enter_accepts_every_default()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"),
            ("src/App.Tests/App.Tests.csproj", "Library", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader("\n\n\n\n\n"), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(0);

            // The prompt list and its order are a stability constraint.
            await AssertAskedInOrder(prompts.ToString());

            Manifest manifest = Parse(workspace);

            await Assert.That(manifest.Build.Preset).IsNull();

            // net10.0 pins no macOS version, so the TFM clause stays silent and the run
            // checks the installed SDK's supported rids - here injected as supporting
            // osx-x64.
            await Assert.That(manifest.Build.RuntimeIdentifiers).IsEquivalentTo(
            [
                "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64",
            ]);

            await Assert.That(manifest.Targets).Count().IsEqualTo(1);
            await Assert.That(manifest.Targets[0].Kinds)
                .IsEquivalentTo([ArtifactKinds.Executables]);
            await Assert.That(manifest.Targets[0].ArchiveFormats)
                .IsEquivalentTo([ArchiveFormats.Zip, ArchiveFormats.TarGz]);

            await Assert.That(manifest.Test).IsNotNull();
            await Assert.That(manifest.Test!.Projects)
                .IsEquivalentTo(["src/App.Tests/App.Tests.csproj"]);
        });
    }

    [Test]
    public async Task The_five_answers_shape_the_manifest()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0-macos.15"),
            ("src/Lib/Lib.csproj", "Library", string.Empty, "true", "net10.0"),
            ("src/App.Tests/App.Tests.csproj", "Library", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            // preset aot; only the first listed project; the portable rid beside a Linux
            // one; tar.xz opted in beside zip; no test section.
            const string answers = "aot\n1\nany-x64, linux-x64\nzip, tar.xz\nnone\n";

            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader(answers), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(0);

            Manifest manifest = Parse(workspace);

            await Assert.That(manifest.Build.Preset).IsEqualTo(BuildPreset.Aot);
            await Assert.That(manifest.Build.RuntimeIdentifiers)
                .IsEquivalentTo(["any-x64", "linux-x64"]);

            await Assert.That(manifest.Targets).Count().IsEqualTo(1);
            await Assert.That(manifest.Targets[0].Project).IsEqualTo("src/App/App.csproj");
            await Assert.That(manifest.Targets[0].ArchiveFormats)
                .IsEquivalentTo([ArchiveFormats.Zip, ArchiveFormats.TarXz]);

            // 'none' on the last question omits the section instead of writing an empty one.
            await Assert.That(manifest.Test).IsNull();
        });
    }

    [Test]
    public async Task An_unknown_preset_answer_is_refused_and_the_question_is_asked_again()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            const string answers = "turbo\n\n\n\n\n\n";

            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader(answers), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Console).Contains("Unknown preset 'turbo'");
            await Assert.That(run.Console).Contains(BuildPreset.Aot);
            await Assert.That(CountOccurrences(prompts.ToString(), "init: build preset"))
                .IsEqualTo(2);

            await Assert.That(Parse(workspace).Build.Preset).IsNull();
        });
    }

    [Test]
    public async Task An_executable_target_cannot_leave_without_a_runtime_identifier()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            // Enter keeps the executable selected, then 'none' is refused for the rid
            // question and a real rid is accepted.
            const string answers = "\n\nnone\nwin-x64\n\n\n";

            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader(answers), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Console).Contains("need at least one runtime identifier");
            await Assert.That(Parse(workspace).Build.RuntimeIdentifiers)
                .IsEquivalentTo(["win-x64"]);
        });
    }

    [Test]
    public async Task An_unknown_rid_and_an_unknown_archive_format_are_each_asked_again()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            const string answers = "\n\nfoo\nosx-arm64\nrar\nnone\n\n";

            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader(answers), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Console).Contains("'foo' is not a selectable runtime identifier");
            await Assert.That(run.Console).Contains("'rar' is not a known archive format");

            Manifest manifest = Parse(workspace);
            await Assert.That(manifest.Build.RuntimeIdentifiers).IsEquivalentTo(["osx-arm64"]);

            // 'none' on its own means archive nothing, written as the explicit opt-out.
            await Assert.That(manifest.Targets[0].ArchiveFormats)
                .IsEquivalentTo([ArchiveFormats.None]);
        });
    }

    [Test]
    public async Task A_number_outside_the_listing_is_refused_rather_than_dropped()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"),
            ("src/App.Tests/App.Tests.csproj", "Library", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            const string answers = "\n9\nnone\n\n\n\n";

            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader(answers), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Console).Contains("'9' is not one of the listed numbers");
            await Assert.That(Parse(workspace).Targets).Count().IsEqualTo(0);
        });
    }

    [Test]
    public async Task The_osx_x64_default_follows_both_clauses_of_the_union_rule()
    {
        // The TFM gates osx-x64 out (macos.26 is not older than macOS 26), so only the
        // installed-SDK clause can light it up.
        const string gatedTfm = "net10.0-macos.26";

        using TestWorkspace withoutSdkSupport = TestWorkspace.Create();
        using TestWorkspace withSdkSupport = TestWorkspace.Create();

        StubProjectInfoProvider withoutSdkProvider = DetectionProvider(
            withoutSdkSupport,
            ("src/App/App.csproj", "Exe", string.Empty, "false", gatedTfm));

        StubProjectInfoProvider withSdkProvider = DetectionProvider(
            withSdkSupport,
            ("src/App/App.csproj", "Exe", string.Empty, "false", gatedTfm));

        string[] withoutSdk =
            await DefaultRidsAsync(withoutSdkSupport, withoutSdkProvider, sdkSupportsOsxX64: false);
        string[] withSdk =
            await DefaultRidsAsync(withSdkSupport, withSdkProvider, sdkSupportsOsxX64: true);

        string[] core = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64"];

        await Assert.That(withoutSdk).IsEquivalentTo(core);
        await Assert.That(withSdk).IsEquivalentTo([.. core, "osx-x64"]);
    }

    [Test]
    public async Task A_non_tty_stdin_fails_naming_the_flag_and_plain_init()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            InitConsole console = new(TextReader.Null, TextWriter.Null, InputIsConsole: false);

            InteractiveRun run = await RunAsync(console, provider, "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(1);
            await Assert.That(run.Console).Contains("--interactive");
            await Assert.That(run.Console).Contains("releasegen init");
            await Assert.That(File.Exists(Path.Combine(
                workspace.Root, ManifestLoader.ManifestFileName))).IsFalse();
        });
    }

    [Test]
    public async Task Input_that_ends_mid_question_aborts_without_writing()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            // An empty script: the first ReadLine already answers with end of input.
            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader(string.Empty), prompts, true),
                provider,
                "init", "--interactive");

            await Assert.That(run.ExitCode).IsEqualTo(1);
            await Assert.That(run.Console).Contains("ended before all five questions");
            await Assert.That(File.Exists(Path.Combine(
                workspace.Root, ManifestLoader.ManifestFileName))).IsFalse();
        });
    }

    [Test]
    public async Task Without_the_flag_init_stays_silent_about_the_questions()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        StubProjectInfoProvider provider = DetectionProvider(
            workspace,
            ("src/App/App.csproj", "Exe", string.Empty, "false", "net10.0"));

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StringWriter prompts = new();
            InteractiveRun run = await RunAsync(
                new InitConsole(new StringReader("\n"), prompts, true),
                provider,
                "init");

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(prompts.ToString()).IsEmpty();

            // The default path writes no rids: an empty list is the minimal skeleton.
            await Assert.That(Parse(workspace).Build.RuntimeIdentifiers).IsEmpty();
        });
    }

    /// <summary>
    /// Invokes init with a scripted console, capturing the CLI's own console (warnings and
    /// errors) separately from the prompt writer the questionnaire writes to.
    /// </summary>
    /// <param name="console">The scripted questionnaire channel.</param>
    /// <param name="provider">The fixed project evaluation detection reads.</param>
    /// <param name="arguments">The argv, for example <c>init --interactive</c>.</param>
    /// <returns>The exit code and both captured outputs.</returns>
    private static async Task<InteractiveRun> RunAsync(
        InitConsole console, IProjectInfoProvider provider, params string[] arguments)
    {
        int exitCode = 0;

        string captured = await ConsoleCapture.CaptureAsync(async () =>
        {
            exitCode = await CliHarness.InvokeAsync(
                new InitCommand(provider, console, () => true), arguments);
        });

        return new InteractiveRun(exitCode, captured);
    }

    /// <summary>
    /// Runs an all-defaults questionnaire in a fresh workspace and returns the rid list the
    /// manifest ended up with, with the installed-SDK clause pinned to the given answer.
    /// </summary>
    /// <param name="workspace">The workspace to run in.</param>
    /// <param name="provider">The fixed project evaluation detection reads.</param>
    /// <param name="sdkSupportsOsxX64">The pinned installed-SDK clause answer.</param>
    /// <returns>The written <c>build.runtimeIdentifiers</c>.</returns>
    private async Task<string[]> DefaultRidsAsync(
        TestWorkspace workspace, StubProjectInfoProvider provider, bool sdkSupportsOsxX64)
    {
        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StringWriter prompts = new();
            InitConsole console = new(new StringReader("\n\n\n\n\n"), prompts, true);

            int exitCode = 0;

            await ConsoleCapture.CaptureAsync(async () =>
            {
                exitCode = await CliHarness.InvokeAsync(
                    new InitCommand(provider, console, () => sdkSupportsOsxX64),
                    "init", "--interactive");
            });

            await Assert.That(exitCode).IsEqualTo(0);
            return true;
        });

        Manifest manifest = _loader.Parse(
            await File.ReadAllTextAsync(Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

        return manifest.Build.RuntimeIdentifiers;
    }

    /// <summary>
    /// Asserts the five questions each appear, in the order they are fixed in.
    /// </summary>
    /// <param name="prompts">Everything the questionnaire wrote.</param>
    /// <returns>A completed assertion set.</returns>
    private static async Task AssertAskedInOrder(string prompts)
    {
        string[] questions =
        [
            "init: build preset",
            "init: projects to release",
            "init: runtime identifiers",
            "init: archive formats",
            "init: test projects",
        ];

        int previous = -1;
        bool everyQuestionAsked = true;

        foreach (string question in questions)
        {
            int index = prompts.IndexOf(question, StringComparison.Ordinal);
            everyQuestionAsked = everyQuestionAsked && index > previous;
            previous = index;
        }

        await Assert.That(everyQuestionAsked).IsTrue();
    }

    /// <summary>
    /// Writes one placeholder project file per entry and returns the provider detection
    /// evaluates against: detection enumerates real files on disk and then consults the stub
    /// keyed by file name, so each test declares both where the project lives and what its
    /// expanded properties answer.
    /// </summary>
    /// <param name="workspace">The workspace to write the project files into.</param>
    /// <param name="projects">One entry per project: relative path, OutputType, PackAsTool, IsPackable, TFM.</param>
    /// <returns>The provider detection evaluates against.</returns>
    private static StubProjectInfoProvider DetectionProvider(
        TestWorkspace workspace,
        params (string RelativePath, string OutputType, string PackAsTool, string IsPackable, string Tfm)[] projects)
    {
        Dictionary<string, ProjectEvaluation> evaluations = [];

        foreach ((
            string relativePath, string outputType, string packAsTool, string isPackable, string tfm)
            in projects)
        {
            workspace.WriteFile(
                relativePath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            evaluations[Path.GetFileName(relativePath)] = new ProjectEvaluation(
                outputType, packAsTool, isPackable)
            {
                TargetFrameworks = [tfm],
            };
        }

        return new StubProjectInfoProvider(evaluations);
    }

    /// <summary>
    /// Parses the written manifest through the same strict loader every stage uses.
    /// </summary>
    /// <param name="workspace">The workspace holding the manifest.</param>
    /// <returns>The parsed manifest.</returns>
    private Manifest Parse(TestWorkspace workspace) =>
        _loader.Parse(File.ReadAllText(
            Path.Combine(workspace.Root, ManifestLoader.ManifestFileName)));

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// One interactive invocation: its exit code and the captured CLI console (warnings and
    /// errors). The prompt writer is read separately by each test, because prompts and CLI
    /// output ride different channels.
    /// </summary>
    /// <param name="ExitCode">The command's exit code.</param>
    /// <param name="Console">The captured CLI console output.</param>
    private sealed record InteractiveRun(int ExitCode, string Console);
}
