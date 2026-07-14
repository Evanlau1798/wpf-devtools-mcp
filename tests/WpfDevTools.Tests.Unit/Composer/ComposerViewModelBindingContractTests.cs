using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerViewModelBindingContractTests
{
    [Fact]
    public void ApplyBlueprint_ShouldPublishAuthoredDataGridBindingRequirement()
    {
        var blueprint = """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "BindingContract",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "wpfui.dataGrid",
                "properties": { "itemsSource": "{Binding Rows}" },
                "slots": { "columns": [{ "kind": "core.template" }] }
              }
            }
            """;

        var result = Apply(PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath(".")), blueprint);

        result.Success.Should().BeTrue();
        using var document = JsonDocument.Parse(result.ViewModelBindingContract.Content);
        var contract = document.RootElement.GetProperty("bindingRequirements");
        contract.GetProperty("status").GetString().Should().Be("required");
        contract.GetProperty("implementationReadiness").GetString()
            .Should().Be("project-implementation-required");
        contract.GetProperty("composerWritesViewModelSource").GetBoolean().Should().BeFalse();
        var requirement = contract.GetProperty("requirements").EnumerateArray().Should().ContainSingle().Subject;
        requirement.GetProperty("bindingPath").GetString().Should().Be("Rows");
        requirement.GetProperty("bindingStatus").GetString().Should().Be("resolved");
        var usage = requirement.GetProperty("usages").EnumerateArray().Should().ContainSingle().Subject;
        usage.GetProperty("jsonPath").GetString().Should().Be("$.layout.properties.itemsSource");
        usage.GetProperty("blockKind").GetString().Should().Be("wpfui.dataGrid");
        usage.GetProperty("propertyName").GetString().Should().Be("itemsSource");
        usage.GetProperty("declaredPropertyType").GetString().Should().Be("binding");
        usage.GetProperty("rawBinding").GetString().Should().Be("{Binding Rows}");
    }

    [Fact]
    public void ApplyBlueprint_ShouldDeduplicateThirdPartyAndExplicitStringBindings()
    {
        var projectRoot = CreateThirdPartyPack();
        try
        {
            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
                ComposerPackPaths.ProjectLocalRoot(projectRoot));
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "ThirdPartyBindings",
                  "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "sample",
                  "layout": {
                    "kind": "sample.panel",
                    "properties": {
                      "captionSource": "{Binding Shared.Rows}",
                      "dataFeed": "{Binding Path=Shared.Rows, Mode=OneWay}",
                      "selectionFeed": "{Binding}"
                    }
                  }
                }
                """;

            var result = Apply(registry, blueprint);

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            using var document = JsonDocument.Parse(result.ViewModelBindingContract.Content);
            var requirements = document.RootElement.GetProperty("bindingRequirements")
                .GetProperty("requirements").EnumerateArray().ToArray();
            requirements.Should().HaveCount(2);
            var resolved = requirements.Single(item => item.GetProperty("bindingStatus").GetString() == "resolved");
            resolved.GetProperty("bindingPath").GetString().Should().Be("Shared.Rows");
            var usages = resolved.GetProperty("usages").EnumerateArray().ToArray();
            usages.Select(item => item.GetProperty("propertyName").GetString())
                .Should().Equal("captionSource", "dataFeed");
            usages.Select(item => item.GetProperty("jsonPath").GetString()).Should().Equal(
                "$.layout.properties.captionSource",
                "$.layout.properties.dataFeed");
            usages[0].GetProperty("declaredPropertyType").GetString().Should().Be("string");
            usages[1].GetProperty("declaredPropertyType").GetString().Should().Be("binding");
            requirements.Single(item => item.GetProperty("bindingStatus").GetString() == "path-unresolved")
                .GetProperty("rawBindings").EnumerateArray().Single().GetString().Should().Be("{Binding}");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldPreserveQuotedAndIndexedBindingPathCommas()
    {
        var projectRoot = CreateThirdPartyPack();
        try
        {
            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
                ComposerPackPaths.ProjectLocalRoot(projectRoot));
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "IndexedBindings",
                  "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "sample",
                  "layout": {
                    "kind": "sample.panel",
                    "properties": {
                      "dataFeed": "{Binding Path='Map[zone,day]', Mode=OneWay}",
                      "selectionFeed": "{Binding Path=Map[region,day], Mode=OneWay}"
                    }
                  }
                }
                """;

            var result = Apply(registry, blueprint);

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            using var document = JsonDocument.Parse(result.ViewModelBindingContract.Content);
            document.RootElement.GetProperty("bindingRequirements").GetProperty("requirements")
                .EnumerateArray()
                .Select(requirement => requirement.GetProperty("bindingPath").GetString())
                .Should().BeEquivalentTo("Map[zone,day]", "Map[region,day]");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static ApplyBlueprintResult Apply(PackRegistry registry, string blueprint)
        => new UiBlueprintApplyService(registry).Apply(
            new ApplyBlueprintRequest(
                blueprint,
                Path.Combine(Path.GetTempPath(), "composer-binding-contract-" + Guid.NewGuid().ToString("N"))));

    private static string CreateThirdPartyPack()
    {
        var projectRoot = TestDirectory.Create();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"control-pack","displayName":"Sample","version":"1.0.0","blocks":["sample.panel"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "panel.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.panel","displayName":"Panel","description":"Binding panel.","category":"container","properties":{"captionSource":{"type":"string"},"dataFeed":{"type":"binding"},"selectionFeed":{"type":"binding"}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"),
            "<Grid Tag=\"{{dataFeed}}\" ToolTip=\"{{captionSource}}\" DataContext=\"{{selectionFeed}}\" />");
        return projectRoot;
    }
}
