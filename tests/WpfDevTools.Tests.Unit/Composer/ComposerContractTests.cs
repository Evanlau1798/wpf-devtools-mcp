using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerContractTests
{
    [Fact]
    public void SchemaVersions_ShouldRecordCurrentCreatorBranchSource()
    {
        UiComposerSchemaVersions.SourceRepository.Should()
            .Be("https://github.com/Evanlau1798/wpf-devtools-extension-pack-creator");
        UiComposerSchemaVersions.SourceRef.Should().Be("master");
        UiComposerSchemaVersions.SourceCommit.Should().Be("9b7b88fa092870994d0121cdc7b436ab633591a4");

        UiComposerSchemaVersions.SchemaFiles.Keys.Should().BeEquivalentTo(
        [
            UiComposerSchemaVersions.UiPack,
            UiComposerSchemaVersions.UiBlock,
            UiComposerSchemaVersions.UiRecipe,
            UiComposerSchemaVersions.UiBlueprint,
            UiComposerSchemaVersions.SourceLock,
            UiComposerSchemaVersions.PackInstallManifest,
            UiComposerSchemaVersions.ComposerProject
        ]);
    }

    [Fact]
    public void SchemaMirrorFiles_ShouldExposeAllComposerContractVersions()
    {
        foreach (var (schemaVersion, fileName) in UiComposerSchemaVersions.SchemaFiles)
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                GetRepoFilePath(Path.Combine("src/WpfDevTools.Mcp.Server/Composer/Schemas", fileName))));

            schema.RootElement.GetProperty("$id").GetString().Should().Be(fileName);
            schema.RootElement.GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetString()
                .Should().Be(schemaVersion);
            schema.RootElement.GetProperty("x-source")
                .GetProperty("commit")
                .GetString()
                .Should().Be(UiComposerSchemaVersions.SourceCommit);
        }
    }

    [Fact]
    public void ComposerJsonLoader_ShouldParseBaselinePackDocuments()
    {
        var pack = Load<UiPackManifest>("packs/builtin/wpfui/0.1.0/pack.json", UiComposerSchemaVersions.UiPack);
        var sourceLock = Load<SourceLock>("packs/builtin/wpfui/0.1.0/source.lock.json", UiComposerSchemaVersions.SourceLock);
        var block = Load<UiBlockDefinition>("packs/builtin/wpfui/0.1.0/blocks/button.block.json", UiComposerSchemaVersions.UiBlock);
        var recipe = Load<UiRecipeDefinition>("packs/builtin/wpfui/0.1.0/recipes/shellWithNavigation.recipe.json", UiComposerSchemaVersions.UiRecipe);
        var blueprint = Load<UiBlueprint>("packs/builtin/wpfui/0.1.0/examples/shell.ui.json", UiComposerSchemaVersions.UiBlueprint);

        pack.Id.Should().Be("wpfui");
        pack.Blocks.Should().HaveCount(16);
        sourceLock.Sources.Should().ContainSingle().Which.Paths.Should().Equal(
            "src/Wpf.Ui",
            "src/Wpf.Ui/Controls/ControlAppearance.cs",
            "src/Wpf.Ui/Controls/SymbolRegular.cs");
        block.Slots.Should().ContainKey("icon")
            .WhoseValue.AllowedKinds.Should().Contain("wpfui.symbolIcon");
        recipe.PackId.Should().Be("wpfui");
        recipe.RequiredPacks.Select(pack => pack.Id).Should().Equal("core", "wpfui");
        blueprint.PrimaryPack.Should().Be("wpfui");
        blueprint.Layout.Kind.Should().Be("wpfui.fluentWindow");
        pack.SourceFilePath.Should().EndWith("pack.json");
        pack.JsonPath.Should().Be("$");
    }

    [Fact]
    public void ComposerJsonLoader_ShouldPreserveCreatorGuidanceFields()
    {
        var pack = ComposerJsonLoader.Parse<UiPackManifest>(
            """
            {
              "schemaVersion": "wpfdevtools.ui-pack.v1",
              "id": "sample",
              "kind": "control-pack",
              "displayName": "Sample",
              "version": "1.0.0",
              "themeTokens": { "accent": "#123456", "spacing": 12 },
              "blocks": []
            }
            """,
            "pack.json",
            UiComposerSchemaVersions.UiPack);
        var recipe = ComposerJsonLoader.Parse<UiRecipeDefinition>(
            """
            {
              "schemaVersion": "wpfdevtools.ui-recipe.v1",
              "id": "sample.workspace",
              "displayName": "Workspace",
              "description": "A flexible workspace starting point.",
              "packId": "sample",
              "inputs": {
                "heading": { "type": "string", "description": "Visible workspace heading." }
              },
              "requiredPacks": [],
              "customizationGuidance": ["Invent the information architecture before choosing blocks."],
              "expandsTo": { "kind": "sample.root" }
            }
            """,
            "workspace.recipe.json",
            UiComposerSchemaVersions.UiRecipe);

        pack.Kind.Should().Be("control-pack");
        pack.ThemeTokens["accent"].GetString().Should().Be("#123456");
        pack.ThemeTokens["spacing"].GetInt32().Should().Be(12);
        recipe.Description.Should().Be("A flexible workspace starting point.");
        recipe.CustomizationGuidance.Should().Equal("Invent the information architecture before choosing blocks.");
        recipe.Inputs["heading"].Description.Should().Be("Visible workspace heading.");
    }

    [Fact]
    public void ComposerJsonLoader_ShouldRejectUnknownSchemaVersion()
    {
        var json = """
        {"schemaVersion":"wpfdevtools.ui-pack.v2","id":"wpfui","version":"0.1.0"}
        """;

        var act = () => ComposerJsonLoader.Parse<UiPackManifest>(
            json,
            "pack.json",
            UiComposerSchemaVersions.UiPack);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*schemaVersion*must be wpfdevtools.ui-pack.v1*");
    }

    [Fact]
    public void ComposerJsonLoader_ShouldParsePackInstallManifestWithMetadata()
    {
        var json = """
        {
          "schemaVersion": "wpfdevtools.pack-install-manifest.v1",
          "id": "wpfui",
          "version": "0.1.0",
          "scope": "composer-builtin",
          "path": "packs/builtin/wpfui/0.1.0",
          "enabled": true,
          "metadata": { "source": "baseline" }
        }
        """;

        var manifest = ComposerJsonLoader.Parse<PackInstallManifest>(
            json,
            "install.manifest.json",
            UiComposerSchemaVersions.PackInstallManifest);

        manifest.Id.Should().Be("wpfui");
        manifest.Enabled.Should().BeTrue();
        manifest.Metadata.Should().ContainKey("source");
    }

    [Fact]
    public void PackInstallManifestSchema_ShouldRequireRuntimeDiscoveryFields()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/Composer/Schemas/wpfdevtools.pack-install-manifest.v1.schema.json")));

        schema.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("schemaVersion", "id", "version", "scope", "path", "enabled");
        schema.RootElement.GetProperty("properties")
            .GetProperty("scope")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("project-local", "user-global", "composer-builtin");
    }

    private static T Load<T>(string relativePath, string expectedSchemaVersion)
        where T : ComposerJsonDocument, new()
        => ComposerJsonLoader.Load<T>(GetRepoFilePath(relativePath), expectedSchemaVersion);

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
