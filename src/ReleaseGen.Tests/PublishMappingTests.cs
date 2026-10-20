namespace ReleaseGen.Tests;

using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenCli.Commands;
using ReleaseGenLib.Models;

/// <summary>
/// The publish stage's invocation mapping (T012, D011): each declared package kind becomes a
/// <c>dotnet nuget push</c> for every staged package, in manifest target order, to the resolved
/// source with the API key from the CLI option or the environment channel - and the integration
/// leg pushes a real package to a local folder source instead of nuget.org (T018).
/// </summary>
public class PublishMappingTests
{
    [Test]
    public async Task No_listed_sources_resolve_to_the_nuget_org_default()
    {
        string[] sources = NuGetSourceResolver.Resolve([]);

        await Assert.That(string.Join(", ", sources)).IsEqualTo(NuGetSourceResolver.DefaultSource);
    }

    [Test]
    public async Task Listed_sources_win_in_manifest_order()
    {
        string[] sources = NuGetSourceResolver.Resolve(["/tmp/first-source", "/tmp/second-source"]);

        await Assert.That(string.Join(", ", sources)).IsEqualTo("/tmp/first-source, /tmp/second-source");
    }

    [Test]
    public async Task A_blank_source_entry_fails_naming_its_position()
    {
        ArgumentException exception = Capture.Exception<ArgumentException>(
            () => NuGetSourceResolver.Resolve(["https://example.test/index.json", " "]));

        await Assert.That(exception.Message).Contains("position 1");
    }

    [Test]
    public async Task Packages_are_pushed_in_manifest_target_order_to_the_manifest_source()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(MultiTargetManifest(source));
        string alphaPackage = StagePackage(workspace, "lib", "Alpha", "Alpha.1.0.0.nupkg");
        string betaPackage = StagePackage(workspace, "tool", "Beta", "Beta.1.0.0.nupkg");
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            using ServiceProvider services = ReleaseGenServices.Create(invoker);

            int exitCode = await CliHarness.InvokeAsync(
                new PublishCommand(services), "publish", "--api-key", "local-key");

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(invoker.Invocations.Count).IsEqualTo(2);

            await Assert.That(Arguments(invoker.Invocations[0])).IsEqualTo(
                $"nuget push {alphaPackage} --source {JsonPath(source)} --api-key local-key");
            await Assert.That(Arguments(invoker.Invocations[1])).IsEqualTo(
                $"nuget push {betaPackage} --source {JsonPath(source)} --api-key local-key");
        });
    }

    [Test]
    public async Task The_executables_kind_is_never_pushed()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(
            $$"""
            {
              "targets": [ { "project": "src/App/App.csproj", "kinds": ["executables", "lib-package"] } ],
              "publish": { "sources": ["{{JsonPath(source)}}"] }
            }
            """);
        string package = StagePackage(workspace, "lib", "App", "App.1.0.0.nupkg");
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            using ServiceProvider services = ReleaseGenServices.Create(invoker);

            int exitCode = await CliHarness.InvokeAsync(
                new PublishCommand(services), "publish", "--api-key", "local-key");

            await Assert.That(exitCode).IsEqualTo(0);

            // D013/D015: release binaries are the build stage's product, not a NuGet package.
            await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
            await Assert.That(Arguments(invoker.Invocations[0])).Contains(package);
        });
    }

    [Test]
    public async Task Missing_package_output_fails_the_stage_before_any_push()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source));
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            using ServiceProvider services = ReleaseGenServices.Create(invoker);

            string console = await ConsoleCapture.CaptureAsync(async () =>
            {
                int exitCode = await CliHarness.InvokeAsync(
                    new PublishCommand(services), "publish", "--api-key", "local-key");

                await Assert.That(exitCode).IsEqualTo(1);
            });

            // The full push list is collected before the first push: no partial upload.
            await Assert.That(console).Contains("no package output");
            await Assert.That(invoker.Invocations.Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task The_api_key_falls_back_to_the_environment_channel()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source));
        StagePackage(workspace, "lib", "Lib", "Lib.1.0.0.nupkg");
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            string? previous = Environment.GetEnvironmentVariable("NUGET_API_KEY");
            Environment.SetEnvironmentVariable("NUGET_API_KEY", "environment-key");

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                int exitCode = await CliHarness.InvokeAsync(new PublishCommand(services), "publish");

                await Assert.That(exitCode).IsEqualTo(0);
                await Assert.That(invoker.Invocations.Count).IsEqualTo(1);
                await Assert.That(Arguments(invoker.Invocations[0]))
                    .Contains("--api-key environment-key");
            }
            finally
            {
                Environment.SetEnvironmentVariable("NUGET_API_KEY", previous);
            }
        });
    }

    [Test]
    public async Task A_missing_api_key_fails_before_any_work()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source));
        StagePackage(workspace, "lib", "Lib", "Lib.1.0.0.nupkg");
        RecordingProcessInvoker invoker = new();

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            string? previous = Environment.GetEnvironmentVariable("NUGET_API_KEY");
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);

            try
            {
                using ServiceProvider services = ReleaseGenServices.Create(invoker);

                string console = await ConsoleCapture.CaptureAsync(async () =>
                {
                    int exitCode = await CliHarness.InvokeAsync(new PublishCommand(services), "publish");

                    await Assert.That(exitCode).IsEqualTo(1);
                });

                // D011: the key travels only through the CLI option or the environment.
                await Assert.That(console).Contains("no API key");
                await Assert.That(console).Contains("NUGET_API_KEY");
                await Assert.That(invoker.Invocations.Count).IsEqualTo(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("NUGET_API_KEY", previous);
            }
        });
    }

    [Test]
    public async Task Integration_a_real_push_lands_a_package_in_a_local_source()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string source = workspace.CreateDirectory("local-source");
        workspace.WriteManifest(PackageManifest(source, project: "Local"));
        TestPackage.WriteNupkg(
            Path.Combine(workspace.Root, "release", "packages", "lib", "Local", "ReleaseGen.LocalPush.1.0.0.nupkg"),
            "ReleaseGen.LocalPush",
            "1.0.0");

        await TestWorkspace.InDirectoryAsync(workspace.Root, async () =>
        {
            StageContext.Reset();
            using ServiceProvider services = ReleaseGenServices.CreateWithRealInvoker();

            // The real invoker runs dotnet nuget push against the local folder source - never
            // nuget.org (T018).
            int exitCode = await CliHarness.InvokeAsync(
                new PublishCommand(services), "publish", "--api-key", "local-key");

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(File.Exists(
                Path.Combine(source, "ReleaseGen.LocalPush.1.0.0.nupkg"))).IsTrue();
        });
    }

    private static string Arguments(ProcessConfiguration configuration) =>
        string.Join(' ', configuration.ArgumentList);

    /// <summary>
    /// Stages a package file where the package stage would have written it.
    /// </summary>
    /// <returns>The staged package's full path.</returns>
    private static string StagePackage(TestWorkspace workspace, string kind, string project, string fileName) =>
        workspace.WriteFile(
            Path.Combine("release", "packages", kind, project, fileName), "staged package");

    private static string PackageManifest(string source, string project = "Lib") =>
        $$"""
        {
          "targets": [ { "project": "src/{{project}}/{{project}}.csproj", "kinds": ["lib-package"] } ],
          "publish": { "sources": ["{{JsonPath(source)}}"] }
        }
        """;

    private static string MultiTargetManifest(string source) =>
        $$"""
        {
          "targets": [
            { "project": "src/Alpha/Alpha.csproj", "kinds": ["lib-package"] },
            { "project": "src/Beta/Beta.csproj", "kinds": ["tool-package"] }
          ],
          "publish": { "sources": ["{{JsonPath(source)}}"] }
        }
        """;

    private static string JsonPath(string path) => path.Replace('\\', '/');
}
