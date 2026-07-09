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
        UiComposerSchemaVersions.SourceRef.Should().Be("fix/source-inventory-coverage-audit");
        UiComposerSchemaVersions.SourceCommit.Should().Be("680cc9d24f3c97f0403defdfff7ef16f63de7c3f");

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
        pack.Blocks.Should().HaveCount(15);
        sourceLock.Sources.Should().ContainSingle().Which.Paths.Should().Equal("src/Wpf.Ui");
        block.Slots.Should().ContainKey("icon")
            .WhoseValue.AllowedKinds.Should().Contain("wpfui.symbolIcon");
        recipe.PackId.Should().Be("wpfui");
        recipe.RequiredPacks.Should().ContainSingle().Which.Id.Should().Be("wpfui");
        blueprint.PrimaryPack.Should().Be("wpfui");
        blueprint.Layout.Kind.Should().Be("wpfui.fluentWindow");
        pack.SourceFilePath.Should().EndWith("pack.json");
        pack.JsonPath.Should().Be("$");
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

    private static T Load<T>(string relativePath, string expectedSchemaVersion)
        where T : ComposerJsonDocument, new()
        => ComposerJsonLoader.Load<T>(GetRepoFilePath(relativePath), expectedSchemaVersion);

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
