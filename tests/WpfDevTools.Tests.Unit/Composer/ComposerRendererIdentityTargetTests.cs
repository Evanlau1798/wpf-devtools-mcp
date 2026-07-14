using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRendererIdentityTargetTests
{
    [Fact]
    public void Renderer_ShouldApplyAuthoredIdentityToPackSelectedTarget()
    {
        var projectRoot = CreatePack("<sample:Panel{{identity.attributes}} />");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(Blueprint(authoredIdentity: true)));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.Xaml.Should().StartWith("<Grid xmlns=");
            result.Xaml.Should().Contain(
                "<sample:Panel x:Name=\"ResultsPanel\" AutomationProperties.AutomationId=\"results-panel\" />");
            result.Xaml[..result.Xaml.IndexOf("<sample:Panel", StringComparison.Ordinal)]
                .Should().NotContain("x:Name=");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldCorrelateGeneratedPreviewNameToPackSelectedTarget()
    {
        var projectRoot = CreatePack("<sample:Panel{{identity.attributes}} />");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(
                    Blueprint(authoredIdentity: false),
                    IncludeTransientElementCorrelation: true));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            var correlation = result.ElementCorrelations.Should().ContainSingle().Subject;
            correlation.JsonPath.Should().Be("$.layout");
            result.Xaml.Should().Contain($"<sample:Panel x:Name=\"{correlation.ElementName}\" />");
            result.Xaml[..result.Xaml.IndexOf("<sample:Panel", StringComparison.Ordinal)]
                .Should().NotContain("x:Name=");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldRejectMultipleIdentityTargetsInOneTemplate()
    {
        var projectRoot = CreatePack(
            "<sample:Panel{{identity.attributes}} /><sample:Panel{{identity.attributes}} />");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(Blueprint(authoredIdentity: true)));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(issue =>
                issue.Code == "RendererIdentityTargetAmbiguous"
                && issue.JsonPath == "$.layout");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldRejectIdentityTargetOutsideStartTag()
    {
        var projectRoot = CreatePack("{{identity.attributes}}<sample:Panel />");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(Blueprint(authoredIdentity: true)));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(issue =>
                issue.Code == "RendererIdentityTargetPlacementInvalid"
                && issue.JsonPath == "$.layout");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldRejectIdentityTargetWithStaticIdentityAttributes()
    {
        var projectRoot = CreatePack(
            "<sample:Panel x:Name=\"StaticPanel\"{{identity.attributes}} />");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(Blueprint(authoredIdentity: true)));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(issue =>
                issue.Code == "RendererIdentityTargetConflict"
                && issue.JsonPath == "$.layout");
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

    private static string CreatePack(string targetMarkup)
    {
        var projectRoot = TestDirectory.Create();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"control-pack","displayName":"Sample","version":"1.0.0","xmlNamespaces":{"sample":"urn:sample"},"blocks":["sample.region"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "install.manifest.json"),
            """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "blocks", "region.block.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.region","displayName":"Region","description":"Composite region.","category":"container","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/region.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "renderers", "xaml", "region.xaml.sbn"),
            $"<Grid>{targetMarkup}</Grid>");
        return projectRoot;
    }

    private static string Blueprint(bool authoredIdentity)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "IdentityTarget",
              "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "sample",
              "layout": {
                "kind": "sample.region"{{(authoredIdentity ? ",\n    \"elementName\": \"ResultsPanel\",\n    \"automationId\": \"results-panel\"" : string.Empty)}}
              }
            }
            """;
}
