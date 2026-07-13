using FluentAssertions;
using MahApps.Metro.Controls;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPackNeutralCodeBehindTests
{
    [Fact]
    public void ApplyBlueprint_ShouldUsePinnedRealMahAppsMetroWindowBaseType()
    {
        var projectRoot = RealExtensionPackFixture.CreateMahAppsProject();
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(projectRoot)).Apply(
                new ApplyBlueprintRequest(RealExtensionPackFixture.MahAppsBlueprint, projectRoot, "Views/OperationsWindow.xaml"));

            result.Success.Should().BeTrue(string.Join(
                Environment.NewLine,
                result.Errors.Select(error => $"{error.Code}: {error.Message}")));
            result.Xaml.Should().Contain("<mah:MetroWindow");
            result.FilePlan.Should().Contain(item =>
                item.Role == "code-behind-integration"
                && item.TargetPath == Path.Combine(projectRoot, "Views", "OperationsWindow.xaml.cs")
                && item.Action.Contains(typeof(MetroWindow).FullName!, StringComparison.Ordinal));
            typeof(MetroWindow).IsSubclassOf(typeof(System.Windows.Window)).Should().BeTrue();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldPlanCustomWindowBaseTypeFromPackRendererMetadata()
    {
        var projectRoot = CreateProjectWithWindowPack("Sample.Controls.ChromeWindow");
        try
        {
            var service = new UiBlueprintApplyService(CreateRegistry(projectRoot));

            var result = service.Apply(new ApplyBlueprintRequest(
                Blueprint(),
                projectRoot,
                "MainWindow.xaml"));

            result.Success.Should().BeTrue(string.Join(
                Environment.NewLine,
                result.Errors.Select(error => $"{error.Code}: {error.Message}")));
            result.Xaml.Should().Contain("x:Class=\"PackNeutralApp.MainWindow\"");
            result.FilePlan.Should().Contain(item =>
                item.Role == "code-behind-integration"
                && item.TargetPath == Path.Combine(projectRoot, "MainWindow.xaml.cs")
                && item.Action.Contains("Sample.Controls.ChromeWindow", StringComparison.Ordinal));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Theory]
    [InlineData("Sample.Controls.ChromeWindow;Remove-Item")]
    [InlineData("ChromeWindow")]
    public void PackLoader_ShouldRejectInvalidCodeBehindBaseType(string baseType)
    {
        var projectRoot = CreateProjectWithWindowPack(baseType);
        try
        {
            var registry = CreateRegistry(projectRoot);

            var result = registry.ListPacks();

            result.Packs.Should().NotContain(pack => pack.Id == "sample");
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Contains("codeBehindBaseType", StringComparison.Ordinal));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldPlanCustomWindowCodeBehindForReviewedTargetPath()
    {
        var projectRoot = CreateProjectWithWindowPack("Sample.Controls.ChromeWindow");
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(projectRoot)).Apply(
                new ApplyBlueprintRequest(Blueprint(), projectRoot, "Views/SettingsWindow.xaml"));

            result.Success.Should().BeTrue();
            result.Xaml.Should().Contain("x:Class=\"PackNeutralApp.SettingsWindow\"");
            result.FilePlan.Should().Contain(item =>
                item.Role == "code-behind-integration"
                && item.TargetPath == Path.Combine(projectRoot, "Views", "SettingsWindow.xaml.cs")
                && item.Action.Contains("Sample.Controls.ChromeWindow", StringComparison.Ordinal));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static PackRegistry CreateRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));

    private static string CreateProjectWithWindowPack(string baseType)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-codebehind-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        File.WriteAllText(
            Path.Combine(projectRoot, "PackNeutralApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
                <RootNamespace>PackNeutralApp</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"style-pack","displayName":"Sample","version":"1.0.0","xmlNamespaces":{"sample":"urn:sample-controls"},"blocks":["sample.window"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "install.manifest.json"),
            """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "blocks", "window.block.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.window","displayName":"Window","description":"Sample custom window.","category":"window","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/window.xaml.sbn","codeBehindBaseType":"{{baseType}}"},"sourceHints":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "renderers", "xaml", "window.xaml.sbn"),
            """
            <sample:ChromeWindow />
            """);
        return projectRoot;
    }

    private static string Blueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "MainWindow",
              "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "sample",
              "layout": { "kind": "sample.window" }
            }
            """;
}
