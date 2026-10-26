namespace ReleaseGen.Cli.Commands;

using System.Globalization;
using ReleaseGenLib.Models;

/// <summary>
/// The five-question questionnaire behind init's <c>--interactive</c> flag: build preset,
/// projects to release, runtime identifiers, archive formats, test projects - asked in that
/// fixed order, every answer defaulting to what detection found (Enter accepts), and every
/// typed answer validated against the same sets the manifest validates against. The prompt
/// list and their order are a stability constraint, the rid answers default through the
/// union rule's rid set [<see cref="Helpers.RidDefaults"/>], and an answer that would leave
/// an executables target without a rid is refused at the prompt rather than at build time.
/// Input that ends mid-question aborts the run with an explanatory error, so nothing is ever
/// written from a half-answered questionnaire.
/// </summary>
internal static class InitInteractive
{
    /// <summary>
    /// The selection that means "nothing selected" in the target and test-project prompts.
    /// </summary>
    private const string NoneAnswer = "none";

    /// <summary>
    /// The error printed when stdin ends before all five questions were answered: it names
    /// the flag, says nothing was written, and points at the non-interactive path.
    /// </summary>
    private const string OutOfInputError =
        "init: the --interactive questionnaire ended before all five questions were answered; " +
        "nothing was written. Run plain 'releasegen init' to write the detected skeleton instead.";

    /// <summary>
    /// The prefix every prompt carries, so the question list reads as one conversation and
    /// each question is identifiable in the captured output.
    /// </summary>
    private const string PromptPrefix = "init: ";

    /// <summary>
    /// Asks the five questions in their fixed order and shapes the answers into the manifest
    /// content init writes: the chosen preset, the chosen targets (each executables target
    /// carrying the chosen archive formats), the chosen rids, and the chosen test projects.
    /// </summary>
    /// <param name="detection">What repo detection found; every default is read from it.</param>
    /// <param name="seededPreset">
    /// The preset <c>--preset</c> named, which becomes question one's default; null when the
    /// flag named none or was omitted.
    /// </param>
    /// <param name="console">The channel answers arrive on and prompts are written to.</param>
    /// <param name="installedSdkSupportsOsxX64">
    /// The installed-SDK clause of the rid default union rule; the real RID-catalog probe when
    /// null, so the default set reflects the machine's SDK.
    /// </param>
    /// <returns>
    /// The questionnaire's result, or null when input ended mid-question (the error is
    /// already printed, and the caller writes nothing).
    /// </returns>
    public static InitAnswers? Run(
        RepoDetection detection,
        string? seededPreset,
        InitConsole console,
        Func<bool>? installedSdkSupportsOsxX64 = null)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(console);

        // Question 1 of 5: the build preset. --preset seeds the default, and only a known
        // name may pass - the same validation the flag path applies, asked again on a miss.
        string presetDefault = seededPreset ?? BuildPreset.None;
        string presetPrompt =
            $"{PromptPrefix}build preset ({string.Join(", ", BuildPreset.All)}) [{presetDefault}]:";

        string? preset;
        bool rejected;

        do
        {
            string? presetAnswer = Ask(console, presetPrompt, [], presetDefault);

            if (presetAnswer is null)
            {
                return null;
            }

            (rejected, preset) = InitCommand.ResolveSeededPreset(presetAnswer);
        }
        while (rejected);

        // Question 2 of 5: which detected projects the manifest releases. Default: all of
        // them - detection is the default path's whole point.
        int targetCount = detection.Targets.Count;
        string targetsDefault = Selection(targetCount);
        string targetsPrompt = targetCount == 0
            ? $"{PromptPrefix}projects to release - nothing was detected, so '{NoneAnswer}' is the only answer [{targetsDefault}]:"
            : $"{PromptPrefix}projects to release (comma-separated numbers, or '{NoneAnswer}') [{targetsDefault}]:";

        List<int>? chosenTargets = AskSelection(
            console, targetsPrompt, TargetListing(detection.Targets), targetsDefault, targetCount);

        if (chosenTargets is null)
        {
            return null;
        }

        // Question 3 of 5: the runtime identifiers, defaulted through the union rule. The
        // answer must leave every chosen executables target with at least one rid.
        string[] ridDefaults =
            RidDefaults.For(detection.TargetFrameworks, installedSdkSupportsOsxX64);
        string ridsDefault = string.Join(", ", ridDefaults);
        string ridsPrompt =
            $"{PromptPrefix}runtime identifiers (comma-separated, or '{NoneAnswer}') [{ridsDefault}]:";
        string ridsHint = $"  selectable: {string.Join(", ", RidDefaults.Selectable)}";
        bool needsRid = chosenTargets
            .Select(index => detection.Targets[index])
            .Any(target => target.Kinds.Contains(ArtifactKinds.Executables));

        string[] rids;

        while (true)
        {
            string? ridsAnswer = Ask(console, ridsPrompt, [ridsHint], ridsDefault);

            if (ridsAnswer is null)
            {
                return null;
            }

            if (TryParseRids(ridsAnswer, out rids, out string ridsError, needsRid))
            {
                break;
            }

            CliOutput.ErrorWithoutHint(ridsError);
        }

        // Question 4 of 5: the archive formats. The default mirrors what the non-interactive
        // skeleton seeds: zip and tar.gz are the absent-key defaults, and tar.xz stays an
        // explicit opt-in that is never defaulted to.
        string archivesDefault = $"{ArchiveFormats.Zip}, {ArchiveFormats.TarGz}";
        string archivesPrompt =
            $"{PromptPrefix}archive formats (comma-separated) [{archivesDefault}]:";
        string archivesHint =
            $"  selectable: {string.Join(", ", ArchiveFormats.All)} - tar.xz is opt-in and never a default";

        string[] archives;

        while (true)
        {
            string? archivesAnswer = Ask(console, archivesPrompt, [archivesHint], archivesDefault);

            if (archivesAnswer is null)
            {
                return null;
            }

            if (TryParseArchives(archivesAnswer, out archives, out string archivesError))
            {
                break;
            }

            CliOutput.ErrorWithoutHint(archivesError);
        }

        // Question 5 of 5: which detected test projects the gate should run. Default: all of
        // them; 'none' writes no test section, which keeps the gate's warn-and-pass default
        // rather than an empty list that would run dotnet test from the current directory.
        int testCount = detection.TestProjects.Count;
        string testsDefault = Selection(testCount);
        string testsPrompt = testCount == 0
            ? $"{PromptPrefix}test projects - nothing was detected, so '{NoneAnswer}' is the only answer [{testsDefault}]:"
            : $"{PromptPrefix}test projects (comma-separated numbers, or '{NoneAnswer}') [{testsDefault}]:";

        List<int>? chosenTests = AskSelection(
            console, testsPrompt, Listing(detection.TestProjects), testsDefault, testCount);

        if (chosenTests is null)
        {
            return null;
        }

        return new InitAnswers(
            preset,
            [.. chosenTargets.Select(index => Copy(detection.Targets[index], archives))],
            rids,
            [.. chosenTests.Select(index => detection.TestProjects[index])]);
    }

    /// <summary>
    /// Asks a numbered-selection question until the answer parses: the question and its
    /// listing print once per attempt, a refusal prints the validator's error before asking
    /// again, and a reader that runs dry aborts the questionnaire.
    /// </summary>
    /// <param name="console">The channel the question is asked on.</param>
    /// <param name="prompt">The question line.</param>
    /// <param name="listing">The candidate lines printed under the question.</param>
    /// <param name="defaultAnswer">What Enter accepts.</param>
    /// <param name="candidateCount">How many candidates the prompt listed.</param>
    /// <returns>
    /// The chosen entries as zero-based indices into the detection lists, deduplicated and in
    /// answer order; null when input ended before an answer arrived.
    /// </returns>
    private static List<int>? AskSelection(
        InitConsole console,
        string prompt,
        string[] listing,
        string defaultAnswer,
        int candidateCount)
    {
        while (true)
        {
            string? answer = Ask(console, prompt, listing, defaultAnswer);

            if (answer is null)
            {
                return null;
            }

            if (TryParseSelection(answer, candidateCount, out List<int> selected, out string error))
            {
                return selected;
            }

            CliOutput.ErrorWithoutHint(error);
        }
    }

    /// <summary>
    /// Writes one prompt - the question line, then any listing or hint lines - and reads the
    /// answer: an empty line (Enter) yields the default, and a line that never comes yields
    /// null after the out-of-input error is printed.
    /// </summary>
    /// <param name="console">The channel the question is asked on.</param>
    /// <param name="prompt">The question line.</param>
    /// <param name="listing">The lines printed under the question before the answer is read.</param>
    /// <param name="defaultAnswer">What Enter accepts.</param>
    /// <returns>The answer, or null when input ended.</returns>
    private static string? Ask(
        InitConsole console, string prompt, string[] listing, string defaultAnswer)
    {
        console.Output.WriteLine(prompt);

        foreach (string listingLine in listing)
        {
            console.Output.WriteLine(listingLine);
        }

        console.Output.Flush();

        string? line = console.Input.ReadLine();

        if (line is null)
        {
            CliOutput.ErrorWithoutHint(OutOfInputError);
            return null;
        }

        string trimmed = line.Trim();
        return trimmed.Length == 0 ? defaultAnswer : trimmed;
    }

    /// <summary>
    /// The default selection line: every candidate's number when candidates exist, otherwise
    /// the <c>none</c> sentinel an empty candidate list accepts.
    /// </summary>
    /// <param name="candidateCount">How many candidates the prompt lists.</param>
    /// <returns>The default answer as it reads inside the prompt's brackets.</returns>
    private static string Selection(int candidateCount) =>
        candidateCount == 0
            ? NoneAnswer
            : string.Join(", ", Enumerable.Range(1, candidateCount));

    /// <summary>
    /// The numbered listing lines for a set of candidate paths.
    /// </summary>
    /// <param name="candidates">The candidate paths, in prompt order.</param>
    /// <returns>The listing lines; empty when there is nothing to list.</returns>
    private static string[] Listing(IEnumerable<string> candidates) =>
        [.. candidates.Select((candidate, index) => $"  {index + 1}. {candidate}")];

    /// <summary>
    /// The numbered listing lines for the detected targets: one line per target carrying its
    /// project path and declared artifact kinds.
    /// </summary>
    /// <param name="targets">The detected targets, in prompt order.</param>
    /// <returns>The listing lines; empty when nothing was detected.</returns>
    private static string[] TargetListing(List<ManifestTarget> targets) =>
    [
        .. targets.Select((target, index) =>
            $"  {index + 1}. {target.Project} ({string.Join(", ", target.Kinds)})"),
    ];

    /// <summary>
    /// Copies one detected target for the manifest, stamping the chosen archive formats onto
    /// the executables-kind ones. Package-kind-only targets stay unarchived - the package
    /// stage's product is never archived - so they carry no key at all.
    /// </summary>
    /// <param name="source">The detected target.</param>
    /// <param name="archives">The archive formats question four chose.</param>
    /// <returns>The manifest-shaped copy.</returns>
    private static ManifestTarget Copy(ManifestTarget source, string[] archives) => new()
    {
        Project = source.Project,
        Kinds = [.. source.Kinds],
        ArchiveFormats = source.Kinds.Contains(ArtifactKinds.Executables) ? [.. archives] : [],
    };

    /// <summary>
    /// Parses a numbered selection - question two's targets and question five's test
    /// projects. <c>none</c> alone clears the selection, numbers are 1-based positions in the
    /// listing, and a number outside the listing is refused rather than silently dropped.
    /// </summary>
    /// <param name="answer">The raw answer, already defaulted when Enter was pressed.</param>
    /// <param name="candidateCount">How many candidates the prompt listed.</param>
    /// <param name="selected">Receives the chosen entries as zero-based indices, in answer order, deduplicated.</param>
    /// <param name="error">The refusal text when the answer does not parse.</param>
    /// <returns><see langword="true"/> when the answer parsed.</returns>
    private static bool TryParseSelection(
        string answer, int candidateCount, out List<int> selected, out string error)
    {
        selected = [];
        string[] tokens = Tokens(answer);

        if (candidateCount == 0)
        {
            error = $"nothing was detected here, so '{NoneAnswer}' is the only answer.";
            return tokens.Length == 1 &&
                   tokens[0].Equals(NoneAnswer, StringComparison.OrdinalIgnoreCase);
        }

        if (tokens.Length == 1 &&
            tokens[0].Equals(NoneAnswer, StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        foreach (string token in tokens)
        {
            if (!int.TryParse(token, CultureInfo.InvariantCulture, out int position) ||
                position < 1 ||
                position > candidateCount)
            {
                error =
                    $"'{token}' is not one of the listed numbers; " +
                    $"enter comma-separated numbers 1-{candidateCount}, or '{NoneAnswer}'.";
                return false;
            }

            // The prompt numbers candidates from 1; the answer is kept as the index the
            // caller will read out of the detection lists.
            int index = position - 1;

            if (!selected.Contains(index))
            {
                selected.Add(index);
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Parses the rid answer: <c>none</c> alone clears the list, every other token must be a
    /// selectable rid, and - when the chosen targets include an executables target - the
    /// result must hold at least one rid, refused at the prompt instead of at build time.
    /// </summary>
    /// <param name="answer">The raw answer, already defaulted when Enter was pressed.</param>
    /// <param name="rids">Receives the chosen rids, in answer order, in canonical spelling.</param>
    /// <param name="error">The refusal text when the answer does not parse.</param>
    /// <param name="needsRid">
    /// Whether the chosen targets include an executables target, which requires at least one
    /// rid.
    /// </param>
    /// <returns><see langword="true"/> when the answer parsed.</returns>
    private static bool TryParseRids(
        string answer, out string[] rids, out string error, bool needsRid)
    {
        rids = [];
        List<string> chosen = [];
        string[] tokens = Tokens(answer);

        // 'none' clears the list; only then does the executables minimum below bite. Any
        // other token must be a rid the prompt offered.
        bool cleared = tokens.Length == 1 &&
                       tokens[0].Equals(NoneAnswer, StringComparison.OrdinalIgnoreCase);

        if (!cleared)
        {
            foreach (string token in tokens)
            {
                string? canonical = RidDefaults.Selectable.FirstOrDefault(selectable =>
                    selectable.Equals(token, StringComparison.OrdinalIgnoreCase));

                if (canonical is null)
                {
                    error =
                        $"'{token}' is not a selectable runtime identifier; " +
                        $"choose from: {string.Join(", ", RidDefaults.Selectable)}.";
                    return false;
                }

                if (!chosen.Contains(canonical, StringComparer.Ordinal))
                {
                    chosen.Add(canonical);
                }
            }
        }

        if (chosen.Count == 0 && needsRid)
        {
            error =
                "the chosen targets include executables, which need at least one runtime " +
                $"identifier; choose at least one of: {string.Join(", ", RidDefaults.Selectable)}.";
            return false;
        }

        rids = [.. chosen];
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Parses the archive-format answer: every token must be a known format, <c>none</c>
    /// stands alone (it means archive nothing), and at least one format is named - tar.xz
    /// passes here as anywhere else, because it is valid in a manifest and only its per-rid
    /// conflict with Windows-class rids is refused at run time.
    /// </summary>
    /// <param name="answer">The raw answer, already defaulted when Enter was pressed.</param>
    /// <param name="formats">Receives the chosen formats, in answer order, in canonical spelling.</param>
    /// <param name="error">The refusal text when the answer does not parse.</param>
    /// <returns><see langword="true"/> when the answer parsed.</returns>
    private static bool TryParseArchives(string answer, out string[] formats, out string error)
    {
        formats = [];
        List<string> chosen = [];

        foreach (string token in Tokens(answer))
        {
            string? canonical = ArchiveFormats.All.FirstOrDefault(known =>
                known.Equals(token, StringComparison.OrdinalIgnoreCase));

            if (canonical is null)
            {
                error =
                    $"'{token}' is not a known archive format; " +
                    $"choose from: {string.Join(", ", ArchiveFormats.All)}.";
                return false;
            }

            if (!chosen.Contains(canonical, StringComparer.Ordinal))
            {
                chosen.Add(canonical);
            }
        }

        if (chosen.Count == 0)
        {
            error = "enter at least one archive format, for example 'zip'.";
            return false;
        }

        if (chosen.Count > 1 && chosen.Contains(ArchiveFormats.None, StringComparer.Ordinal))
        {
            error =
                $"'{ArchiveFormats.None}' cannot be combined with other archive formats; " +
                $"enter '{ArchiveFormats.None}' on its own to archive nothing.";
            return false;
        }

        formats = [.. chosen];
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Splits an answer into tokens on commas and whitespace, so <c>1, 2</c>, <c>1,2</c> and
    /// <c>1 2</c> all read as two answers. Candidate numbers, rids, and formats never contain
    /// a separator, which is why the split is safe for every selection prompt.
    /// </summary>
    /// <param name="answer">The raw answer.</param>
    /// <returns>The non-empty, trimmed tokens.</returns>
    private static string[] Tokens(string answer) =>
        answer.Split(
            [' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
