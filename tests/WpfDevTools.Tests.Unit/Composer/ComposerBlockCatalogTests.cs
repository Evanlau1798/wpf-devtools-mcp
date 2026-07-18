using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlockCatalogTests
{
    [Fact]
    public void BlockCatalog_ShouldExposeWpfUiBlocksWithDtoFields()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCatalog(new BlockCatalogQuery());

        result.Items.Count(item => item.PackId == "wpfui").Should().Be(16);
        var button = result.Items.Single(item => item.Kind == "wpfui.button");
        button.PackId.Should().Be("wpfui");
        button.PackVersion.Should().Be("0.1.0");
        button.DisplayName.Should().Be("Button");
        button.Category.Should().Be("input");
        button.Properties.Keys.Should().Contain(["appearance", "text"]);
        button.Slots.Keys.Should().Contain("icon");
        button.AllowedKinds.Should().Contain("wpfui.symbolIcon");
        button.RendererAvailable.Should().BeTrue();
        button.SourceHintSummary.Should().Contain("src/Wpf.Ui/Controls/Button/Button.cs");
        button.CompositionSkeleton!.Value.GetProperty("kind").GetString()
            .Should().Be("wpfui.button");
    }

    [Fact]
    public void BuiltInBlockCatalog_ShouldExposeCompleteAuthoringContracts()
    {
        var items = CreateCatalog().GetCatalog(new BlockCatalogQuery()).Items;

        var missingDescriptions = items
            .SelectMany(item => item.Properties
                .Where(property => string.IsNullOrWhiteSpace(property.Value.Description))
                .Select(property => $"{item.Kind}.properties.{property.Key}"))
            .Concat(items.SelectMany(item => item.Slots
                .Where(slot => string.IsNullOrWhiteSpace(slot.Value.Description))
                .Select(slot => $"{item.Kind}.slots.{slot.Key}")))
            .ToArray();

        missingDescriptions.Should().BeEmpty(
            "built-in packs should explain every authoring choice without library-specific guessing");

        FindSingleValueSlotContractIssues().Should().BeEmpty(
            "slots rendered into single-value WPF surfaces must reject extra children before rendering");
    }

    [Fact]
    public async Task GetUiBlockCatalogTool_ShouldExposePackDefinedAuthoringHints()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["core"],
                kind: "core.stack",
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            var stack = result.StructuredContent!.Value.GetProperty("items")[0];
            stack.GetProperty("description").GetString().Should()
                .Contain("vertically or horizontally");
            var spacing = stack.GetProperty("properties").GetProperty("spacing");
            spacing.GetProperty("description").GetString().Should()
                .Contain("each child");
            spacing.GetProperty("previewWarning").GetString().Should()
                .Contain("final app");
            stack.GetProperty("slots").GetProperty("children")
                .GetProperty("description").GetString().Should()
                .Contain("Ordered child");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GetUiBlockCatalogTool_ShouldExposeCompactProjection()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var full = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["core"],
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);
            var compact = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["core"],
                compact: true,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            var fullPayload = full.StructuredContent!.Value;
            var compactPayload = compact.StructuredContent!.Value;
            compactPayload.GetProperty("compact").GetBoolean().Should().BeTrue();
            compactPayload.GetProperty("itemCount").GetInt32().Should()
                .Be(fullPayload.GetProperty("itemCount").GetInt32());
            compactPayload.GetRawText().Length.Should().BeLessThan(fullPayload.GetRawText().Length / 2);

            var stack = compactPayload.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("kind").GetString() == "core.stack");
            stack.GetProperty("propertyNames").EnumerateArray().Select(value => value.GetString())
                .Should().Contain("spacing");
            stack.GetProperty("propertyWarnings").GetProperty("spacing").GetString().Should()
                .Contain("final app");
            var children = stack.GetProperty("slots").GetProperty("children");
            children.GetProperty("allowedKinds").EnumerateArray().Select(value => value.GetString())
                .Should().Contain("*");
            children.GetProperty("minItems").GetInt32().Should().Be(0);
            children.TryGetProperty("maxItems", out _).Should().BeFalse();
            stack.GetProperty("compositionSkeleton").GetProperty("kind").GetString().Should().Be("core.stack");
            stack.GetProperty("description").GetString().Should()
                .Contain("vertically or horizontally");
            stack.TryGetProperty("properties", out _).Should().BeFalse();
            stack.TryGetProperty("sourceHintSummary", out _).Should().BeFalse();

            var text = compactPayload.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("kind").GetString() == "core.text");
            text.GetProperty("authoringRoles").EnumerateArray().Select(value => value.GetString())
                .Should().Contain("text-run");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void BlockCatalog_ShouldFilterByPackCategoryKindPrefixComposableAndDetail()
    {
        var catalog = CreateCatalog();

        var filtered = catalog.GetCatalog(new BlockCatalogQuery(
            PackIds: ["wpfui"],
            Category: "navigation",
            KindPrefix: "wpfui.navigation",
            ComposableOnly: true,
            Kind: null));
        var detail = catalog.GetCatalog(new BlockCatalogQuery(Kind: "wpfui.navigationViewItem"));

        filtered.Items.Select(item => item.Kind).Should()
            .BeEquivalentTo(
                "wpfui.navigationView",
                "wpfui.navigationViewDemo",
                "wpfui.navigationViewItem",
                "wpfui.navigationViewItemSeparator");
        detail.Items.Should().ContainSingle(item => item.Kind == "wpfui.navigationViewItem");
    }

    [Theory]
    [InlineData("wpfui.navigationView", "items", "wpfui.navigationViewItem")]
    [InlineData("wpfui.navigationView", "items", "wpfui.navigationViewItemSeparator")]
    [InlineData("wpfui.navigationViewItem", "icon", "wpfui.symbolIcon")]
    [InlineData("wpfui.tabView", "items", "wpfui.tabViewItem")]
    [InlineData("wpfui.fluentWindow", "titleBar", "wpfui.titleBar")]
    public void BlockCatalog_ShouldExposeSlotCompositionRules(string kind, string slot, string allowedKind)
    {
        var catalog = CreateCatalog();

        var item = catalog.GetCatalog(new BlockCatalogQuery(Kind: kind)).Items.Single();

        item.Slots[slot].AllowedKinds.Should().Contain(allowedKind);
    }

    [Fact]
    public void BlockCatalog_ShouldAvoidLongSourceCodeAndOptionalSurfaceBlocks()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCatalog(new BlockCatalogQuery());
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);

        serialized.Should().NotContain("namespace Wpf.Ui");
        serialized.Should().NotContain("public class");
        result.Items.Select(item => item.Kind).Should().NotContain([
            "wpfui.calendar",
            "wpfui.gallery",
            "wpfui.syntaxHighlight",
            "wpfui.tray",
            "wpfui.template"
        ]);
    }

    [Fact]
    public async Task GetUiBlockCatalogTool_ShouldReturnPackNeutralCompositionSkeleton()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectRoot = Path.Combine(tempRoot, "project");
            CreateRenderableSamplePack(projectRoot);
            var result = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["sample"],
                kind: "sample.panel",
                projectRoot: projectRoot,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("itemCount").GetInt32().Should().Be(1);
            payload.TryGetProperty("compositionExamples", out _).Should().BeFalse();
            payload.TryGetProperty("compositionExampleCount", out _).Should().BeFalse();
            var skeleton = payload.GetProperty("items")[0].GetProperty("compositionSkeleton");
            skeleton.GetProperty("kind").GetString().Should().Be("sample.panel");
            skeleton.GetProperty("properties").GetProperty("title").GetString().Should().Be("Panel");
            skeleton.GetProperty("properties").GetProperty("visible").GetBoolean().Should().BeFalse();
            skeleton.GetProperty("properties").GetProperty("margin").GetString().Should().Be("0");
            skeleton.GetProperty("properties").GetProperty("rowHeight").GetString().Should().Be("Auto");
            skeleton.GetProperty("properties").GetProperty("offset").GetInt32().Should().Be(-1);
            skeleton.GetProperty("properties").GetProperty("settings").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
            skeleton.GetProperty("properties").GetProperty("mode").GetString().Should().Be("compact");
            skeleton.GetProperty("slots").GetProperty("content").GetArrayLength().Should().Be(0);

            var blueprintJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = "wpfdevtools.ui-blueprint.v1",
                name = "SampleSkeleton",
                packs = new[] { new { id = "sample", version = "1.0.0", required = true, role = "primary" } },
                primaryPack = "sample",
                layout = skeleton
            });
            var validation = await UiComposerMcpTools.ValidateUiBlueprint(
                blueprintJson,
                projectRoot: projectRoot,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);
            var validationPayload = validation.StructuredContent!.Value;
            validationPayload.GetProperty("valid").GetBoolean().Should().BeTrue(validationPayload.GetRawText());

            var render = await UiComposerMcpTools.RenderUiBlueprint(
                blueprintJson,
                targetPath: "CatalogSkeleton.xaml",
                projectRoot: projectRoot,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);
            var renderPayload = render.StructuredContent!.Value;
            renderPayload.GetProperty("success").GetBoolean().Should().BeTrue(renderPayload.GetRawText());
            renderPayload.GetProperty("xaml").GetString().Should().Contain("Title=\"Panel\"");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static BlockCatalogService CreateCatalog()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        return new BlockCatalogService(registry);
    }

    private static IReadOnlyList<string> FindSingleValueSlotContractIssues()
    {
        var issues = new List<string>();
        var root = TestRepositoryPaths.GetRepoFilePath("packs/builtin");
        foreach (var blockPath in Directory.EnumerateFiles(root, "*.block.json", SearchOption.AllDirectories))
        {
            using var block = JsonDocument.Parse(File.ReadAllText(blockPath));
            var value = block.RootElement;
            var kind = value.GetProperty("kind").GetString();
            var packRoot = Directory.GetParent(Path.GetDirectoryName(blockPath)!)!.FullName;
            var rendererPath = value.GetProperty("renderer").GetProperty("xamlTemplate").GetString()!;
            var renderer = File.ReadAllText(Path.Combine(packRoot, rendererPath));
            var slots = value.GetProperty("slots");

            foreach (var slot in slots.EnumerateObject())
            {
                var maxItems = slot.Value.TryGetProperty("maxItems", out var maximum)
                    ? maximum.GetInt32()
                    : (int?)null;
                var token = Regex.Escape($"{{{{slot.{slot.Name}}}}}");
                var directSingleContent = Regex.IsMatch(
                        renderer,
                        $@"<(?:Border|ContentControl)\b[^>]*>\s*{token}\s*</(?:Border|ContentControl)>",
                        RegexOptions.CultureInvariant)
                    || Regex.IsMatch(
                        renderer,
                        $@"<[\w:]+\.(?:Content|Header|Icon|TitleBar)>\s*{token}\s*</[\w:]+\.(?:Content|Header|Icon|TitleBar)>",
                        RegexOptions.CultureInvariant);
                if (directSingleContent && maxItems != 1)
                {
                    issues.Add($"{kind}.slots.{slot.Name}");
                }
            }
        }

        return issues;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateRenderableSamplePack(string projectRoot)
    {
        var root = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(root, "blocks"));
        Directory.CreateDirectory(Path.Combine(root, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(root, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","displayName":"Sample","version":"1.0.0","blocks":["sample.panel"],"recipes":[],"xmlNamespaces":{"sample":"urn:sample-controls"}}""");
        File.WriteAllText(Path.Combine(root, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Sample","url":"https://example.invalid/sample","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(root, "blocks", "panel.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.panel","displayName":"Panel","category":"container","properties":{"title":{"type":"string","required":true,"default":"Panel"},"visible":{"type":"bool","required":true,"default":"invalid"},"margin":{"type":"string","format":"thickness","required":true,"default":"invalid"},"rowHeight":{"type":"string","format":"gridLength","required":true},"offset":{"type":"number","required":true,"integer":true,"maximum":-1,"default":4.5},"settings":{"type":"object","required":true,"default":[]},"mode":{"type":"string","required":true,"allowedValues":["compact","wide"],"default":"invalid"}},"slots":{"content":{"allowedKinds":["*"]}},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "panel.xaml.sbn"),
            "<sample:Panel Title=\"{{title}}\">{{slot.content}}</sample:Panel>");
        var escapedRoot = root.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(root, "install.manifest.json"),
            $$"""{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":"{{escapedRoot}}","enabled":true}""");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
