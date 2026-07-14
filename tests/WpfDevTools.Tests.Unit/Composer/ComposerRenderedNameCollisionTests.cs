using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRenderedNameCollisionTests
{
    [Fact]
    public void Renderer_ShouldRejectRepeatedThirdPartyNamesInOneNamescope()
    {
        var projectRoot = CreatePack("<Grid x:Name=\"SharedPart\" />");
        try
        {
            var result = Render(projectRoot);

            result.Success.Should().BeFalse();
            var issue = result.Errors.Should().ContainSingle(item =>
                item.Code == "RenderedNameCollision").Subject;
            issue.JsonPath.Should().Be("$.layout.slots.children[1]");
            issue.RelatedJsonPaths.Should().Equal(
                "$.layout.slots.children[0]",
                "$.layout.slots.children[1]");
            result.SourceMap.Where(entry => entry.BlockKind == "sample.namedPart")
                .Select(entry => entry.StartIndex).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldAllowRepeatedNamesInSeparateTemplateNamescopes()
    {
        var projectRoot = CreatePack(
            "<ContentControl><ContentControl.ContentTemplate><DataTemplate><Grid x:Name=\"SharedPart\" /></DataTemplate></ContentControl.ContentTemplate></ContentControl>");
        try
        {
            var result = Render(projectRoot);

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.Errors.Should().NotContain(issue => issue.Code == "RenderedNameCollision");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldNotInferThirdPartyNamescopeFromTypeName()
    {
        var projectRoot = CreatePack(
            "<sample:CardTemplate><Grid x:Name=\"SharedPart\" /></sample:CardTemplate>");
        try
        {
            var result = Render(projectRoot);

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.Code == "RenderedNameCollision");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldHonorPackDeclaredCustomNamescopeElement()
    {
        var projectRoot = CreatePack(
            "<sample:CardTemplate><Grid x:Name=\"SharedPart\" /></sample:CardTemplate>",
            nameScopeElement: "CardTemplate");
        try
        {
            var result = Render(projectRoot);

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.Errors.Should().NotContain(issue => issue.Code == "RenderedNameCollision");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("sample:CardTemplate")]
    [InlineData("\u00C9lement")]
    public void Renderer_ShouldRejectInvalidPackDeclaredNamescopeElement(string nameScopeElement)
    {
        var projectRoot = CreatePack(
            "<sample:CardTemplate><Grid x:Name=\"SharedPart\" /></sample:CardTemplate>",
            nameScopeElement);
        try
        {
            var packRoot = Path.Combine(
                projectRoot,
                ".wpfdevtools",
                "packs",
                "sample",
                "1.0.0");
            var act = () => ComposerPackLoader.Load(packRoot);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*InvalidRendererNameScopeElements*");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewTool_ShouldPublishBothRenderedNameCollisionPaths()
    {
        var projectRoot = CreatePack("<Grid x:Name=\"SharedPart\" />");
        try
        {
            using var sessionManager = new SessionManager();

            var result = await UiComposerMcpTools.PreviewUiBlueprint(
                sessionManager,
                Blueprint(),
                restoreEnabled: false,
                projectRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            var diagnostic = result.StructuredContent!.Value.GetProperty("diagnostics")
                .EnumerateArray()
                .Single(item => item.GetProperty("code").GetString() == "RenderedNameCollision");
            diagnostic.GetProperty("relatedJsonPaths")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Should().Equal(
                    "$.layout.slots.children[0]",
                    "$.layout.slots.children[1]");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public async Task ApplyTool_ShouldPublishBothRenderedNameCollisionPaths()
    {
        var projectRoot = CreatePack("<Grid x:Name=\"SharedPart\" />");
        try
        {
            var result = await UiComposerMcpTools.ApplyUiBlueprint(
                Blueprint(),
                projectRoot,
                dryRun: true,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            var issue = result.StructuredContent!.Value.GetProperty("errors")
                .EnumerateArray()
                .Single(item => item.GetProperty("code").GetString() == "RenderedNameCollision");
            issue.GetProperty("relatedJsonPaths")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Should().Equal(
                    "$.layout.slots.children[0]",
                    "$.layout.slots.children[1]");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static RenderBlueprintResult Render(string projectRoot)
    {
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));
        return new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(Blueprint()));
    }

    private static string CreatePack(string childRenderer, string? nameScopeElement = null)
    {
        var projectRoot = TestDirectory.Create();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"control-pack","displayName":"Sample","version":"1.0.0","xmlNamespaces":{"sample":"urn:sample"},"blocks":["sample.host","sample.namedPart"],"recipes":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "host.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.host","displayName":"Host","description":"Host.","category":"container","properties":{},"slots":{"children":{"allowedKinds":["sample.namedPart"]}},"renderer":{"xamlTemplate":"renderers/xaml/host.xaml.sbn"},"sourceHints":[]}""");
        var nameScopeMetadata = nameScopeElement is null
            ? string.Empty
            : $",\"nameScopeElements\":[\"{nameScopeElement}\"]";
        File.WriteAllText(Path.Combine(packRoot, "blocks", "named-part.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.namedPart","displayName":"Named part","description":"Named part.","category":"display","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/named-part.xaml.sbn"__NAMESCOPE__},"sourceHints":[]}"""
                .Replace("__NAMESCOPE__", nameScopeMetadata, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "host.xaml.sbn"),
            "<StackPanel>{{slot.children}}</StackPanel>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "named-part.xaml.sbn"), childRenderer);
        return projectRoot;
    }

    private static string Blueprint()
        => """
           {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"RepeatedNames","packs":[{"id":"sample","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"sample","layout":{"kind":"sample.host","slots":{"children":[{"kind":"sample.namedPart"},{"kind":"sample.namedPart"}]}}}
           """;
}
