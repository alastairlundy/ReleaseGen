using ReleaseGenLib.Exceptions;

namespace ReleaseGen.Tests;

using ReleaseGenLib;
using ReleaseGenLib.Models;

/// <summary>
/// The tool-package kind cross-check against the real Microsoft.Build evaluation path: the
/// expanded <c>PackAsTool</c> property is what validates a <c>tool-package</c> target, never
/// raw project text, so a declaration in the project file and one in a
/// <c>Directory.Build.props</c> both validate while a project declaring neither fails naming
/// the fix. Nothing here passes the property as a build switch - the project file stays the
/// only place it can be set.
/// </summary>
public class PackAsToolValidationTests
{
    private readonly ProjectInfoProvider _provider = new();
    private readonly ManifestLoader _loader;

    /// <summary>
    /// The tool-package kind cross-check against the real Microsoft.Build evaluation path: the
    /// expanded <c>PackAsTool</c> property is what validates a <c>tool-package</c> target, never
    /// raw project text, so a declaration in the project file and one in a
    /// <c>Directory.Build.props</c> both validate while a project declaring neither fails naming
    /// the fix.
    /// </summary>
    public PackAsToolValidationTests() => _loader = new ManifestLoader(_provider);

    [Test]
    public async Task Pack_as_tool_declared_in_the_project_file_validates_a_tool_package_target()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string projectPath = workspace.WriteFile(
            Path.Combine("src", "Tool", "Tool.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackAsTool>true</PackAsTool>
              </PropertyGroup>
            </Project>
            """);
        FileInfo manifestFile = new(workspace.WriteManifest(ToolPackageManifest()));

        ProjectEvaluation evaluation = _provider.EvaluateProject(new FileInfo(projectPath));
        await Assert.That(evaluation.PackAsToolIsTrue).IsTrue();

        Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(manifestFile.FullName));
        _loader.Validate(manifest, manifestFile);
    }

    [Test]
    public async Task Pack_as_tool_declared_in_a_props_file_validates_a_tool_package_target()
    {
        using TestWorkspace workspace = TestWorkspace.Create();

        // The declaration lives above the project file: expansion, not project-file text, is
        // the whole basis of the check, so this setup must validate exactly like an inline one.
        workspace.WriteFile(
            "Directory.Build.props",
            """
            <Project>
              <PropertyGroup>
                <PackAsTool>true</PackAsTool>
              </PropertyGroup>
            </Project>
            """);
        string projectPath = workspace.WriteFile(
            Path.Combine("src", "Tool", "Tool.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        FileInfo manifestFile = new(workspace.WriteManifest(ToolPackageManifest()));

        ProjectEvaluation evaluation = _provider.EvaluateProject(new FileInfo(projectPath));
        await Assert.That(evaluation.PackAsTool).IsEqualTo("true");
        await Assert.That(evaluation.PackAsToolIsTrue).IsTrue();

        Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(manifestFile.FullName));
        _loader.Validate(manifest, manifestFile);
    }

    [Test]
    public async Task A_tool_package_target_whose_project_never_sets_pack_as_tool_fails_naming_the_fix()
    {
        using TestWorkspace workspace = TestWorkspace.Create();
        string projectPath = workspace.WriteFile(
            Path.Combine("src", "Tool", "Tool.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        FileInfo manifestFile = new(workspace.WriteManifest(ToolPackageManifest()));

        Manifest manifest = _loader.Parse(await File.ReadAllTextAsync(manifestFile.FullName));

        ManifestException exception = Capture.Exception<ManifestException>(
            () => _loader.Validate(manifest, manifestFile));

        // The failure converts a silent broken package into a named fix: the target, the
        // project, the property, and a declaration site that does not assume the project file.
        await Assert.That(exception.Message).Contains(ArtifactKinds.ToolPackage);
        await Assert.That(exception.Message).Contains(new FileInfo(projectPath).FullName);
        await Assert.That(exception.Message).Contains("PackAsTool");
        await Assert.That(exception.Message).Contains("(unset)");
        await Assert.That(exception.Message).Contains("Directory.Build.props");
    }

    private static string ToolPackageManifest() =>
        """
        {
          "targets": [ { "project": "src/Tool/Tool.csproj", "kinds": ["tool-package"] } ],
          "publish": { "sources": [] }
        }
        """;
}
