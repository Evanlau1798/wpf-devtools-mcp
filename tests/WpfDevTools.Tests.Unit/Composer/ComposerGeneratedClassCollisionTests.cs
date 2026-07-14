using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerGeneratedClassCollisionTests
{
    [Theory]
    [InlineData("\"elementName\": \"KilnLedgerWindow\",", "ContentRoot", "$.layout.elementName")]
    [InlineData("", "KilnLedgerWindow", "$.layout.slots.content[0].elementName")]
    public void RenderBlueprint_ShouldRejectGeneratedClassMemberNameCollision(
        string rootIdentity,
        string childName,
        string expectedPath)
    {
        var blueprint = WindowBlueprint(rootIdentity, childName);

        var result = new UiBlueprintRenderer(CreateBuiltinRegistry()).Render(
            new RenderBlueprintRequest(blueprint, "Views/KilnLedgerWindow.xaml"));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(issue =>
            issue.Code == "GeneratedClassMemberNameCollision"
            && issue.JsonPath == expectedPath
            && issue.AllowedValues.Count == 0);
    }

    [Fact]
    public void ApplyBlueprint_ShouldRejectCollisionWithSanitizedTargetClassName()
    {
        var projectRoot = TestDirectory.Create();
        try
        {
            var result = new UiBlueprintApplyService(CreateBuiltinRegistry()).Apply(
                new ApplyBlueprintRequest(
                    WindowBlueprint("\"elementName\": \"Heat_map\",", "ContentRoot"),
                    projectRoot,
                    "Views/Heat-map.xaml"));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(issue =>
                issue.Code == "GeneratedClassMemberNameCollision"
                && issue.JsonPath == "$.layout.elementName");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldAllowMatchingNameForThirdPartyBlockWithoutCodeBehind()
    {
        var projectRoot = CreateProjectWithPanelPack();
        try
        {
            var result = new UiBlueprintRenderer(CreateProjectRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(PanelBlueprint(), "Views/SettingsView.xaml", projectRoot));

            result.Success.Should().BeTrue(string.Join(
                Environment.NewLine,
                result.Errors.Select(issue => $"{issue.Code}: {issue.Message}")));
            result.Xaml.Should().Contain("x:Name=\"SettingsView\"");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static PackRegistry CreateBuiltinRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static PackRegistry CreateProjectRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));

    private static string WindowBlueprint(string rootIdentity, string childName)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "KilnLedgerWindow",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "wpfui.fluentWindow",
                {{rootIdentity}}
                "slots": {
                  "content": [{
                    "kind": "wpfui.card",
                    "elementName": "{{childName}}"
                  }]
                }
              }
            }
            """;

    private static string PanelBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SettingsView",
              "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "sample",
              "layout": {
                "kind": "sample.panel",
                "elementName": "SettingsView"
              }
            }
            """;

    private static string CreateProjectWithPanelPack()
    {
        var projectRoot = TestDirectory.Create();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"style-pack","displayName":"Sample","version":"1.0.0","xmlNamespaces":{"sample":"urn:sample"},"blocks":["sample.panel"],"recipes":[]}
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
            Path.Combine(packRoot, "blocks", "panel.block.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.panel","displayName":"Panel","description":"Third-party panel.","category":"layout","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"),
            """
            <sample:Panel />
            """);
        return projectRoot;
    }
}
