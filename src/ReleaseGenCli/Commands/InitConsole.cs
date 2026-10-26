namespace ReleaseGen.Cli.Commands;

/// <summary>
/// The console the init questionnaire works against: the reader answers arrive on, the writer
/// prompts go to, and whether stdin is a console at all. The last part is what
/// <c>--interactive</c> gates on - with piped or redirected stdin there is nobody to answer
/// the five prompts, so init fails with an explanatory error instead of blocking on a read
/// [DECISIONS-ReleaseGen-adoption-friction.md#D005]. The process console is the production
/// channel; tests inject a scripted reader and a capture writer instead of touching it.
/// </summary>
/// <param name="Input">The reader the prompt loop reads answers from.</param>
/// <param name="Output">The writer the prompt loop writes questions to.</param>
/// <param name="InputIsConsole">
/// Whether stdin is an interactive console; <see langword="false"/> for piped or redirected
/// input.
/// </param>
public sealed record InitConsole(TextReader Input, TextWriter Output, bool InputIsConsole);
