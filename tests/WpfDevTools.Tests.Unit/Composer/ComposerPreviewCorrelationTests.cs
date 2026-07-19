using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewCorrelationTests
{
    [Fact]
    public void Renderer_ShouldAddTransientNamesOnlyWhenPreviewCorrelationIsRequested()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var normal = renderer.Render(new RenderBlueprintRequest(Blueprint()));
        var preview = renderer.Render(new RenderBlueprintRequest(
            Blueprint(),
            IncludeTransientElementCorrelation: true));

        normal.Success.Should().BeTrue(normal.Errors.FirstOrDefault()?.Message);
        normal.Xaml.Should().NotContain("WpfDevToolsBp_");
        normal.ElementCorrelations.Should().BeEmpty();
        preview.Success.Should().BeTrue(preview.Errors.FirstOrDefault()?.Message);
        preview.ElementCorrelations.Should().HaveCount(2);
        preview.ElementCorrelations.Select(item => item.JsonPath).Should().BeEquivalentTo(
            "$.layout",
            "$.layout.slots.children[0]");
        preview.ElementCorrelations.Select(item => item.ElementName).Should().OnlyHaveUniqueItems();
        preview.Xaml.Should().Contain("x:Name=\"WpfDevToolsBp_");
    }

    [Fact]
    public async Task Preview_ShouldReturnTransientRendererRootCorrelationWithoutChangingBlueprint()
    {
        var result = await new UiBlueprintPreviewService(CreateRegistry()).PreviewAsync(
            new PreviewBlueprintRequest(Blueprint(), RestoreEnabled: false),
            CancellationToken.None);

        result.Valid.Should().BeTrue(result.Diagnostics.FirstOrDefault()?.Message);
        result.ElementCorrelations.Should().HaveCount(2);
        result.ElementCorrelations.Should().OnlyContain(item =>
            item.ElementName.StartsWith("WpfDevToolsBp_", StringComparison.Ordinal));
        Blueprint().Should().NotContain("WpfDevToolsBp_");
    }

    [Fact]
    public void Renderer_ShouldAvoidNamesReservedByPackTemplates()
    {
        var projectRoot = CreateNamedPack("WpfDevToolsBp_0000");
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(NamedBlueprint(), IncludeTransientElementCorrelation: true));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.ElementCorrelations.Select(item => item.ElementName).Should().OnlyHaveUniqueItems();
            result.ElementCorrelations.Should().Contain(item => item.ElementName == "WpfDevToolsBp_0000");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void CorrelationLookupPlan_ShouldCoverGeneratedAndRendererProvidedNames()
    {
        var projectRoot = CreateNamedPack("RendererRoot");
        try
        {
            var render = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(NamedBlueprint(), IncludeTransientElementCorrelation: true));

            var plan = UiBlueprintPreviewDiagnosticsBridge.BuildCorrelationLookupPlan(render.ElementCorrelations);

            plan.Should().Contain(item => item.Query == "WpfDevToolsBp_" && item.MatchMode == "contains");
            plan.Should().Contain(item => item.Query == "RendererRoot" && item.MatchMode == "exact");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldExcludePackDeclaredNonInspectableRootsFromPreviewCorrelation()
    {
        var result = new UiBlueprintRenderer(CreateRegistry()).Render(
            new RenderBlueprintRequest(GridBlueprint(), IncludeTransientElementCorrelation: true));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.ElementCorrelations.Should().ContainSingle()
            .Which.BlockKind.Should().Be("core.grid");
        result.Xaml.Should().NotContain("<RowDefinition Height=\"Auto\" MinHeight=\"0\" MaxHeight=\"1000000\" x:Name=");
        result.Xaml.Should().NotContain("<ColumnDefinition Width=\"*\" MinWidth=\"0\" MaxWidth=\"1000000\" x:Name=");
    }

    [Fact]
    public void Renderer_ShouldPreserveAuthoredIdentityForProjectPackNonInspectableTarget()
    {
        var projectRoot = CreateNonInspectablePack();
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(
                    NonInspectableBlueprint(),
                    IncludeTransientElementCorrelation: true));

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.ElementCorrelations.Should().BeEmpty();
            result.Xaml.Should().Contain("x:Name=\"AuthoredDefinition\"")
                .And.Contain("AutomationProperties.AutomationId=\"definition-target\"")
                .And.NotContain("WpfDevToolsBp_");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void NameScopeDiagnostics_ShouldBeSkippedWhenNoCorrelationsExist()
    {
        UiBlueprintPreviewDiagnosticsBridge.RequiresNameScopeDiagnostics([]).Should().BeFalse();
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static PackRegistry CreateRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));

    private static string CreateNamedPack(string rootName)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-correlation-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "named", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"named","kind":"control-pack","displayName":"Named","version":"1.0.0","blocks":["named.root","named.child"],"recipes":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"named","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "root.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"named.root","displayName":"Root","category":"container","properties":{},"slots":{"content":{"allowedKinds":["named.child"]}},"renderer":{"xamlTemplate":"renderers/xaml/root.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "child.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"named.child","displayName":"Child","category":"display","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/child.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "root.xaml.sbn"),
            $"<Grid x:Name=\"{rootName}\">{{{{slot.content}}}}</Grid>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "child.xaml.sbn"), "<TextBlock />");
        return projectRoot;
    }

    private static string CreateNonInspectablePack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-noninspectable-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"layout-pack","displayName":"Sample","version":"1.0.0","blocks":["sample.definition"],"recipes":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "definition.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.definition","displayName":"Definition","category":"layout-definition","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/definition.xaml.sbn","runtimeInspectable":false},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "definition.xaml.sbn"),
            "<RowDefinition {{identity.attributes}} />");
        return projectRoot;
    }

    private static string NamedBlueprint()
        => """
           {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"Named","packs":[{"id":"named","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"named","layout":{"kind":"named.root","slots":{"content":[{"kind":"named.child"}]}}}
           """;

    private static string Blueprint()
        => """
           {
             "schemaVersion": "wpfdevtools.ui-blueprint.v1",
             "name": "PreviewCorrelation",
             "packs": [
               { "id": "core", "version": "0.1.0", "required": true, "role": "primary" }
             ],
             "primaryPack": "core",
             "layout": {
               "kind": "core.stack",
               "slots": {
                 "children": [
                   { "kind": "core.text", "properties": { "text": "Correlated" } }
                 ]
               }
             }
           }
           """;

    private static string NonInspectableBlueprint()
        => """
           {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"Definition","packs":[{"id":"sample","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"sample","layout":{"kind":"sample.definition","elementName":"AuthoredDefinition","automationId":"definition-target"}}
           """;

    private static string GridBlueprint()
        => """
           {
             "schemaVersion": "wpfdevtools.ui-blueprint.v1",
             "name": "PreviewGridCorrelation",
             "packs": [
               { "id": "core", "version": "0.1.0", "required": true, "role": "primary" }
             ],
             "primaryPack": "core",
             "layout": {
               "kind": "core.grid",
               "slots": {
                 "rows": [
                   { "kind": "core.rowDefinition", "properties": { "height": "Auto" } }
                 ],
                 "columns": [
                   { "kind": "core.columnDefinition", "properties": { "width": "*" } }
                 ]
               }
             }
           }
           """;
}
