using CliInvoke.Core;
using Microsoft.Extensions.DependencyInjection;
using ReleaseGenCli.Helpers;
using ReleaseGenLib.Abstractions;

namespace ReleaseGen.Tests;

using ReleaseGenCli.Commands;

/// <summary>
/// The publish stage's real-invoker integration leg: a real <c>dotnet nuget push</c> lands a
/// package in a local folder source instead of nuget.org. Composed from the production
/// container via <see cref="ReleaseGenRealInvokerDiAttribute"/> - the only difference from the
/// recording suites is that no invoker is faked.
/// </summary>
[ReleaseGenRealInvokerDi]
public class PublishIntegrationTests
{
    private readonly IManifestLoader _manifestLoader;
    private readonly IProcessInvoker _processInvoker;
    
    /// <summary>
    /// The publish stage's real-invoker integration leg: a real <c>dotnet nuget push</c> lands a
    /// package in a local folder source instead of nuget.org. Composed from the production
    /// container via <see cref="ReleaseGenRealInvokerDiAttribute"/> - the only difference from the
    /// recording suites is that no invoker is faked.
    /// </summary>
    public PublishIntegrationTests(ServiceProvider services)
    {
        _manifestLoader = services.GetRequiredService<IManifestLoader>();
        _processInvoker = services.GetRequiredService<IProcessInvoker>();
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

            // The real invoker runs dotnet nuget push against the local folder source - never
            // nuget.org.
            int exitCode = await CliHarness.InvokeAsync(
                new PublishCommand(_manifestLoader, _processInvoker), "publish", "--api-key", "local-key");

            await Assert.That(exitCode).IsEqualTo(0);
            await Assert.That(File.Exists(
                Path.Combine(source, "ReleaseGen.LocalPush.1.0.0.nupkg"))).IsTrue();
        });
    }

    private static string PackageManifest(string source, string project) =>
        $$"""
        {
          "targets": [ { "project": "src/{{project}}/{{project}}.csproj", "kinds": ["lib-package"] } ],
          "publish": { "sources": ["{{JsonPath(source)}}"] }
        }
        """;

    private static string JsonPath(string path) => path.Replace('\\', '/');
}
