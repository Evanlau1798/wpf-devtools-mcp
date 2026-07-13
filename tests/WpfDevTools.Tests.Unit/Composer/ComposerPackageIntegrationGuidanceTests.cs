using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPackageIntegrationGuidanceTests
{
    private static readonly RequiredNuGetPackage[] Packages =
    [
        new("Nebula.Controls", "2.4.0")
    ];

    [Fact]
    public void Create_ShouldUseCentralPackageDeclarationWhenProjectEnablesCpm()
    {
        var projectRoot = CreateProject(
            """<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup /></Project>""",
            """<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>""");
        try
        {
            var result = PackageIntegrationPlanner.Create(projectRoot, Packages);

            result.Mode.Should().Be("central");
            result.ProjectInspected.Should().BeTrue();
            result.CentralPackageFile.Should().Be("Directory.Packages.props");
            result.Packages.Should().ContainSingle();
            result.Packages[0].ProjectPackageReference.Should().Be("<PackageReference Include=\"Nebula.Controls\" />");
            result.Packages[0].CentralPackageVersion.Should().Be("<PackageVersion Include=\"Nebula.Controls\" Version=\"2.4.0\" />");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public void Create_ShouldUseInlineVersionWhenProjectDoesNotEnableCpm()
    {
        var projectRoot = CreateProject("<Project />");
        try
        {
            var result = PackageIntegrationPlanner.Create(projectRoot, Packages);

            result.Mode.Should().Be("project");
            result.ProjectFile.Should().Be("Sample.csproj");
            result.Packages[0].ProjectPackageReference
                .Should().Be("<PackageReference Include=\"Nebula.Controls\" Version=\"2.4.0\" />");
            result.Packages[0].CentralPackageVersion.Should().BeNull();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public void Create_ShouldAvoidAuthoritativeSnippetWhenProjectModeIsUnknown()
    {
        var result = PackageIntegrationPlanner.Create(projectRoot: null, Packages);

        result.Mode.Should().Be("unknown");
        result.ProjectInspected.Should().BeFalse();
        result.InspectionConfidence.Should().Be("none");
        result.InspectionReason.Should().Contain("No target project file");
        result.Packages[0].ProjectPackageReference.Should().BeNull();
        result.Packages[0].CentralPackageVersion.Should().BeNull();
    }

    [Fact]
    public async Task RenderTool_ShouldExposeGuidanceDerivedFromTargetProject()
    {
        var projectRoot = CreateProject(
            """<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>""",
            """<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>""");
        try
        {
            var result = await UiComposerMcpTools.RenderUiBlueprint(
                WpfUiBlueprint(),
                projectRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var guidance = result.StructuredContent!.Value.GetProperty("packageIntegrationGuidance");
            guidance.GetProperty("mode").GetString().Should().Be("central");
            guidance.GetProperty("packages")[0].GetProperty("projectPackageReference").GetString()
                .Should().Be("<PackageReference Include=\"WPF-UI\" />");

            var apply = await UiComposerMcpTools.ApplyUiBlueprint(
                WpfUiBlueprint(),
                projectRoot,
                dryRun: true,
                cancellationToken: CancellationToken.None);
            apply.StructuredContent!.Value.GetProperty("packageIntegrationGuidance")
                .GetProperty("mode").GetString().Should().Be("central");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private static string CreateProject(string projectXml, string? centralXml = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "wpfdevtools-packages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Sample.csproj"), projectXml);
        if (centralXml is not null)
        {
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), centralXml);
        }

        return root;
    }

    private static string WpfUiBlueprint()
        => """
           {
             "schemaVersion": "wpfdevtools.ui-blueprint.v1",
             "name": "PackageGuidance",
             "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
             "primaryPack": "wpfui",
             "layout": { "kind": "wpfui.button", "properties": { "text": "Install" } }
           }
           """;
}
