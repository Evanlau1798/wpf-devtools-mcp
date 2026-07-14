using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPropertyVocabularyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("wpfui.button", "appearance", "NotARealAppearance", "Primary")]
    [InlineData("wpfui.symbolIcon", "symbol", "Temperature", "Temperature24")]
    public void ValidateBlueprint_ShouldRejectPackageOwnedPropertyValue(
        string kind,
        string property,
        string value,
        string expectedSuggestion)
    {
        var result = new BlueprintValidationService(CreateBuiltinRegistry()).Validate(
            Blueprint(kind, property, value));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(issue =>
            issue.Code == "PropertyValueNotAllowed"
            && issue.JsonPath == $"$.layout.properties.{property}"
            && issue.AllowedValues.Contains(expectedSuggestion, StringComparer.Ordinal));
        result.Errors.Single(issue => issue.Code == "PropertyValueNotAllowed")
            .AllowedValues.Should().HaveCountLessThanOrEqualTo(12);
    }

    [Fact]
    public void GetCatalog_BroadQuery_ShouldSummarizeLargePropertyVocabulary()
    {
        var result = new BlockCatalogService(CreateBuiltinRegistry()).GetCatalog(
            new BlockCatalogQuery(["wpfui"]));

        var property = result.Items.Single(item => item.Kind == "wpfui.symbolIcon").Properties["symbol"];
        var json = JsonSerializer.SerializeToElement(property, JsonOptions);
        json.GetProperty("allowedValueCount").GetInt32().Should().BeGreaterThan(9_000);
        json.GetProperty("allowedValuesTruncated").GetBoolean().Should().BeTrue();
        json.GetProperty("allowedValues").GetArrayLength().Should().Be(12);
    }

    [Fact]
    public void GetCatalog_WhitespaceKind_ShouldSummarizeLargePropertyVocabulary()
    {
        var result = new BlockCatalogService(CreateBuiltinRegistry()).GetCatalog(
            new BlockCatalogQuery(["wpfui"], Kind: "   "));

        var property = result.Items.Single(item => item.Kind == "wpfui.symbolIcon").Properties["symbol"];
        var json = JsonSerializer.SerializeToElement(property, JsonOptions);
        json.GetProperty("allowedValuesTruncated").GetBoolean().Should().BeTrue();
        json.GetProperty("allowedValues").GetArrayLength().Should().Be(12);
    }

    [Fact]
    public void GetCatalog_ExactKind_ShouldReturnCompletePropertyVocabulary()
    {
        var result = new BlockCatalogService(CreateBuiltinRegistry()).GetCatalog(
            new BlockCatalogQuery(["wpfui"], Kind: "wpfui.symbolIcon"));

        var property = result.Items.Single().Properties["symbol"];
        var json = JsonSerializer.SerializeToElement(property, JsonOptions);
        json.GetProperty("allowedValueCount").GetInt32().Should().BeGreaterThan(9_000);
        json.GetProperty("allowedValuesTruncated").GetBoolean().Should().BeFalse();
        property.AllowedValues.Should().Contain("Temperature24");
        property.AllowedValues.Should().NotContain("Temperature");
    }

    [Fact]
    public async Task GetUiBlockCatalogTool_ExactKind_ShouldPublishCompleteVocabulary()
    {
        var localAppData = TestDirectory.Create();
        try
        {
            var result = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["wpfui"],
                kind: "wpfui.symbolIcon",
                localAppDataRoot: localAppData,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var property = result.StructuredContent!.Value
                .GetProperty("items")[0]
                .GetProperty("properties")
                .GetProperty("symbol");
            property.GetProperty("allowedValueCount").GetInt32().Should().BeGreaterThan(9_000);
            property.GetProperty("allowedValuesTruncated").GetBoolean().Should().BeFalse();
            property.GetProperty("allowedValues").EnumerateArray()
                .Select(value => value.GetString())
                .Should().Contain("Temperature24");
        }
        finally
        {
            TestDirectory.Delete(localAppData);
        }
    }

    [Fact]
    public void PackLoader_ShouldHydratePackRelativePropertyVocabulary()
    {
        var packRoot = CreateVocabularyPack("vocabularies/modes.json", "compact", "wide");
        try
        {
            var pack = ComposerPackLoader.LoadUncachedForValidation(packRoot);

            pack.Blocks.Single().Properties["mode"].AllowedValues
                .Should().Equal("compact", "wide");
        }
        finally
        {
            TestDirectory.Delete(Path.GetFullPath(Path.Combine(packRoot, "..", "..")));
        }
    }

    [Theory]
    [InlineData("../outside.json", "*allowedValuesPath*escapes pack root*")]
    [InlineData("vocabularies/../blocks/panel.block.json", "*allowedValuesPath*stay under vocabularies/*")]
    public void PackLoader_ShouldRejectPropertyVocabularyOutsideVocabularyRoot(
        string allowedValuesPath,
        string expectedMessage)
    {
        var packRoot = CreateVocabularyPack(allowedValuesPath, "compact");
        try
        {
            var act = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            act.Should().Throw<InvalidDataException>()
                .WithMessage(expectedMessage);
        }
        finally
        {
            TestDirectory.Delete(Path.GetFullPath(Path.Combine(packRoot, "..", "..")));
        }
    }

    [Fact]
    public void PackFingerprint_ShouldChangeWhenPropertyVocabularyChanges()
    {
        var packRoot = CreateVocabularyPack("vocabularies/modes.json", "compact");
        try
        {
            ComposerPackLoader.ClearCacheForTests();
            var first = ComposerPackLoader.LoadWithFingerprint(packRoot);
            File.WriteAllText(
                Path.Combine(packRoot, "vocabularies", "modes.json"),
                "[\"compact\",\"wide\"]");

            var second = ComposerPackLoader.LoadWithFingerprint(packRoot);

            second.FromCache.Should().BeFalse();
            second.Fingerprint.Should().NotBe(first.Fingerprint);
            second.Pack.Blocks.Single().Properties["mode"].AllowedValues
                .Should().Equal("compact", "wide");
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            TestDirectory.Delete(Path.GetFullPath(Path.Combine(packRoot, "..", "..")));
        }
    }

    private static PackRegistry CreateBuiltinRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint(string kind, string property, string value)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "VocabularyDemo",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "{{kind}}",
                "properties": { "{{property}}": "{{value}}" }
              }
            }
            """;

    private static string CreateVocabularyPack(string allowedValuesPath, params string[] values)
    {
        var root = TestDirectory.Create();
        var packRoot = Path.Combine(root, "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        Directory.CreateDirectory(Path.Combine(packRoot, "vocabularies"));
        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"style-pack","displayName":"Sample","version":"1.0.0","blocks":["sample.panel"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "blocks", "panel.block.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.panel","displayName":"Panel","description":"Panel.","category":"layout","properties":{"mode":{"type":"string","allowedValuesPath":"__PATH__"}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}
            """.Replace("__PATH__", allowedValuesPath, StringComparison.Ordinal));
        File.WriteAllText(
            Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"),
            "<Grid />");
        File.WriteAllText(
            Path.Combine(packRoot, "vocabularies", "modes.json"),
            JsonSerializer.Serialize(values));
        return packRoot;
    }
}
