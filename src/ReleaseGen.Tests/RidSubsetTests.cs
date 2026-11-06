using Microsoft.Extensions.DependencyInjection;
using ReleaseGen.Cli.Commands;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Abstractions;

/// <summary>
/// The build stage's <c>--rids</c> subset: a comma-separated, per-invocation restriction of
/// the manifest's <c>runtimeIdentifiers</c> - it can only carve out a subset, never add a rid
/// the manifest does not declare (same escape posture as <c>--preset</c>: the flag never
/// writes the manifest). Valid subsets plan and run only the named rids; an empty value, a
/// blank entry, and an unknown value are refused with exit 1 before any planning, the refusal
/// naming the offending value and the manifest's own set. case-insensitive matching keeps the
/// manifest's spelling authoritative for output paths.
/// </summary>
[ReleaseGenDi]
public class RidSubsetTests
{
    private readonly RecordingProcessInvoker _invoker;
    private readonly IManifestLoader _manifestLoader;
    private readonly IBuildStageRunner _buildStageRunner;

    public RidSubsetTests(ServiceProvider services, RecordingProcessInvoker invoker)
    {
        _invoker = invoker;
        _manifestLoader = services.GetRequiredService<IManifestLoader>();
        _buildStageRunner = services.GetRequiredService<IBuildStageRunner>();
    }

    private const string ManifestWithThreeRids = """
        {
          "targets": [ { "project": "src/Lib/Lib.csproj", "kinds": ["executables"] } ],
          "build": { "preset": "aot", "runtimeIdentifiers": ["win-x64", "win-arm64", "linux-x64"] },
          "publish": { "sources": [] }
        }
        """;

    /// <summary>
    /// The position of one flag in a recorded invocation's argument list; the recording
    /// invoker hands back read-only argument lists, so the position search is here.
    /// </summary>
    private static int ArgumentIndex(System.Collections.Generic.IReadOnlyList<string> arguments, string flag)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], flag, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Flag '{flag}' not found in the invocation's arguments.");
    }

    private static string Compact(string console) =>
        string.Concat(console.Where(static c => !char.IsWhiteSpace(c)));

    [Test]
    public async Task A_rid_subset_plans_only_the_named_rids_in_a_dry_run()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(ManifestWithThreeRids);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner),
                    "build", "--dry-run", "--rids", "win-x64,win-arm64");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // Only the subset is planned: the two named rids appear, the excluded one never
            // does, and the summary counts the subset rather than the manifest's full set.
            // Spectre wraps long lines, so command-line assertions run on a whitespace-free
            // copy; '-r win-x64' becomes '-rwin-x64'.
            string compact = Compact(console);
            await Assert.That(compact).Contains("-rwin-x64");
            await Assert.That(compact).Contains("-rwin-arm64");
            await Assert.That(compact.Contains("linux-x64"))
                .IsFalse();
            await Assert.That(console).Contains("planned 1 target(s) x 2 runtime identifier(s)");
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_rid_subset_runs_and_archives_only_the_named_rids()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(ManifestWithThreeRids);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            // A fake dotnet publish materializes the output folder a real publish would have
            // written, so the stage's archiver can archive it for real.
            _invoker.OnInvocation = configuration =>
            {
                int outputIndex = ArgumentIndex(configuration.ArgumentList, "-o");
                Directory.CreateDirectory(configuration.ArgumentList[outputIndex + 1]);
            };

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner),
                    "build", "--rids", "win-x64,win-arm64");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            string compact = Compact(console);
            string[] executedRids = _invoker.Invocations
                .Select(static invocation =>
                    invocation.ArgumentList[ArgumentIndex(invocation.ArgumentList, "-r") + 1])
                .Distinct()
                .ToArray();

            await Assert.That(executedRids).IsEquivalentTo(["win-x64", "win-arm64"]);
            await Assert.That(compact.Contains("linux-x64")).IsFalse();
            await Assert.That(console).Contains("published 1 target(s) x 2 runtime identifier(s)");

            // Each subset rid's binaries and default-format archive landed beside the rid
            // folder, under the invocation directory.
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release", "win-x64", "Lib")))
                .IsTrue();
            await Assert.That(Directory.Exists(Path.Combine(workspace.Root, "release", "win-arm64", "Lib")))
                .IsTrue();
            await Assert.That(File.Exists(Path.Combine(workspace.Root, "release", "Lib-win-x64.zip")))
                .IsTrue();
            await Assert.That(File.Exists(Path.Combine(workspace.Root, "release", "Lib-win-arm64.zip")))
                .IsTrue();
            await Assert.That(File.Exists(Path.Combine(workspace.Root, "release", "Lib-linux-x64.zip")))
                .IsFalse();
        });
    }

    [Test]
    public async Task An_unknown_rid_is_refused_naming_the_value_and_the_manifest_set()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(ManifestWithThreeRids);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner),
                    "build", "--dry-run", "--rids", "win-x64,linux-musl-x64");

                // Refused like an unknown preset: exit 1, no plan printed, no invocations.
                await Assert.That(exitCode).IsEqualTo(1);
            });

            // The refusal names the offending value and the manifest's own rid set, so the
            // fix is obvious without a second lookup.
            await Assert.That(console).Contains("linux-musl-x64");
            await Assert.That(console).Contains(
                "not in the manifest's runtimeIdentifiers (win-x64, win-arm64, linux-x64)");
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_blank_rids_entry_is_refused()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(ManifestWithThreeRids);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner),
                    "build", "--dry-run", "--rids", "win-x64,,linux-x64");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(console).Contains("blank");
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task An_empty_rids_value_is_refused()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(ManifestWithThreeRids);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner),
                    "build", "--dry-run", "--rids", "");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            await Assert.That(console).Contains("names no runtime identifier");
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task Duplicate_and_case_variant_rid_entries_collapse_to_the_manifest_spelling()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        workspace.WriteManifest(ManifestWithThreeRids);

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new BuildCommand(_manifestLoader, _buildStageRunner),
                    "build", "--dry-run", "--rids", "WIN-X64,Win-x64");

                await Assert.That(exitCode).IsEqualTo(0);
            });

            // One rid, planned once, written with the manifest's spelling.
            string compact = Compact(console);
            await Assert.That(compact).Contains("-rwin-x64");
            await Assert.That(console).Contains("planned 1 target(s) x 1 runtime identifier(s)");
            await Assert.That(compact.Contains("win-arm64")).IsFalse();
            await Assert.That(_invoker.Invocations.Count).IsEqualTo(0);
        });
    }
}
