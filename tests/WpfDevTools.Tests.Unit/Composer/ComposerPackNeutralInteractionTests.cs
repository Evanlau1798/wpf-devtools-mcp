using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPackNeutralInteractionTests
{
    [Fact]
    public void ApplyBlueprint_ShouldUsePackInteractionPropertyMappings()
    {
        var projectRoot = CreateProjectWithInteractionPack("execute");
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(projectRoot)).Apply(
                new ApplyBlueprintRequest(Blueprint(), projectRoot));

            result.Success.Should().BeTrue(string.Join(
                Environment.NewLine,
                result.Errors.Select(error => $"{error.Code}: {error.Message}")));
            var interaction = result.BehaviorIntegrationContract.Interactions.Should().ContainSingle().Subject;
            interaction.Kind.Should().Be("navigation");
            interaction.CommandPath.Should().Be("OpenRegionCommand");
            interaction.CommandParameter.Should().Be("region-7");
            interaction.TargetPageTag.Should().Be("region-detail");
            interaction.Label.Should().Be("Open region");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void PackLoader_ShouldRejectInteractionMappingToUnknownProperty()
    {
        var projectRoot = CreateProjectWithInteractionPack("missingCommand");
        try
        {
            var result = CreateRegistry(projectRoot).ListPacks();

            result.Packs.Should().NotContain(pack => pack.Id == "sample.behaviors");
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Contains("commandProperty", StringComparison.Ordinal));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ApplyBlueprint_ShouldPreserveComplexCommandBindingAsRequiredInteraction()
    {
        var projectRoot = CreateProjectWithInteractionPack("execute");
        const string binding = "{Binding DataContext.OpenRegionCommand, RelativeSource={RelativeSource AncestorType=Window}}";
        try
        {
            var result = new UiBlueprintApplyService(CreateRegistry(projectRoot)).Apply(
                new ApplyBlueprintRequest(Blueprint(binding), projectRoot));

            result.Success.Should().BeTrue();
            result.BehaviorIntegrationContract.Status.Should().Be("required");
            var interaction = result.BehaviorIntegrationContract.Interactions.Should().ContainSingle().Subject;
            interaction.BindingStatus.Should().Be("path-unresolved");
            interaction.CommandBinding.Should().Be(binding);
            interaction.CommandPath.Should().BeNull();
            interaction.ImplementationGuidance.Should().Contain("raw command binding");
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

    private static string CreateProjectWithInteractionPack(string commandProperty)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-interaction-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample.behaviors", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample.behaviors","kind":"control-pack","displayName":"Sample Behaviors","version":"1.0.0","blocks":["sample.behaviors.trigger"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "install.manifest.json"),
            """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample.behaviors","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "blocks", "trigger.block.json"),
            $$"""
            {
              "schemaVersion":"wpfdevtools.ui-block.v1",
              "kind":"sample.behaviors.trigger",
              "displayName":"Trigger",
              "description":"A mapped interaction.",
              "category":"interaction",
              "properties":{
                "execute":{"type":"binding"},
                "payload":{"type":"string"},
                "destination":{"type":"string"},
                "caption":{"type":"string"}
              },
              "slots":{},
              "interaction":{
                "kind":"navigation",
                "commandProperty":"{{commandProperty}}",
                "commandParameterProperty":"payload",
                "targetProperty":"destination",
                "labelProperty":"caption"
              },
              "renderer":{"xamlTemplate":"renderers/xaml/trigger.xaml.sbn"},
              "sourceHints":[]
            }
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "trigger.xaml.sbn"), "<Button />");
        return projectRoot;
    }

    private static string Blueprint(string commandBinding = "{Binding OpenRegionCommand}")
        => $$"""
            {
              "schemaVersion":"wpfdevtools.ui-blueprint.v1",
              "name":"Interaction",
              "packs":[{"id":"sample.behaviors","version":"1.0.0","required":true,"role":"primary"}],
              "primaryPack":"sample.behaviors",
              "layout":{
                "kind":"sample.behaviors.trigger",
                "properties":{
                  "execute":"{{commandBinding}}",
                  "payload":"region-7",
                  "destination":"region-detail",
                  "caption":"Open region"
                }
              }
            }
            """;
}
