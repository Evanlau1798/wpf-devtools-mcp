using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerMultiPackValidationTests
{
    private const string WpfUiResourceDictionary = "<ui:ControlsDictionary />";

    [Fact]
    public void ValidateBlueprint_ShouldReportDeclaredPackVersionConflict()
    {
        var result = CreateValidator().Validate("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "VersionConflict",
              "packs": [
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                { "id": "wpfui", "version": "9.9.9", "required": false, "role": "control-pack" }
              ],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """);

        result.Errors.Should().Contain(issue => issue.JsonPath == "$.packs[1].version"
            && issue.Code == "PackVersionConflict"
            && issue.Message.Contains("0.1.0", StringComparison.Ordinal)
            && issue.Message.Contains("9.9.9", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlueprint_ShouldWarnForPackResourceConflicts()
    {
        var projectRoot = CreateTempProjectWithResourcePack("conflict.resources", WpfUiResourceDictionary);
        try
        {
            var result = CreateValidator(projectRoot).Validate("""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "ResourceConflict",
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "conflict.resources", "version": "1.0.0", "required": false, "role": "control-pack" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": { "kind": "wpfui.button" }
                }
                """);

            result.Errors.Should().BeEmpty();
            result.Warnings.Should().Contain(issue => issue.JsonPath == "$.packs"
                && issue.Code == "PackResourceConflict"
                && issue.Message.Contains(WpfUiResourceDictionary, StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldRejectMultipleVisualPacksWithStructuredConflict()
    {
        var projectRoot = CreateTempProjectWithPackKind("other.theme", "style-pack");
        try
        {
            var result = CreateValidator(projectRoot).Validate("""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "VisualConflict",
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "other.theme", "version": "1.0.0", "required": false, "role": "extension" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": { "kind": "wpfui.button" }
                }
                """);

            result.Errors.Should().ContainSingle(issue => issue.Code == "MultipleVisualPacks");
            result.Resolution.Conflicts.Should().ContainSingle(conflict =>
                conflict.Code == "MultipleVisualPacks"
                && conflict.Severity == "error"
                && conflict.PackIds.SequenceEqual(new[] { "other.theme", "wpfui" }));
            result.Resolution.Packs.Should().Contain(pack =>
                pack.Id == "other.theme"
                && pack.Scope == "project-local"
                && pack.Status == "resolved");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task ValidationAndRenderer_ShouldExposeTheSameDeclaredResourceOrder()
    {
        var projectRoot = CreateTempProjectWithResourcePacks(
            ("z.resources", "control-pack", new[] { "Z" }),
            ("a.resources", "control-pack", new[] { "A" }));
        const string blueprint = """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ResourceOrder",
              "packs": [
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                { "id": "z.resources", "version": "1.0.0", "required": false, "role": "control-pack" },
                { "id": "a.resources", "version": "1.0.0", "required": false, "role": "control-pack" }
              ],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """;
        try
        {
            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
                ComposerPackPaths.ProjectLocalRoot(projectRoot));
            var validation = new BlueprintValidationService(registry).Validate(blueprint);
            var render = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(blueprint));
            var tool = await UiComposerMcpTools.ValidateUiBlueprint(
                blueprint,
                projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            validation.Errors.Should().BeEmpty();
            validation.Resolution.ResourceOrder.Where(resource => resource is "Z" or "A").Should().Equal("Z", "A");
            render.RequiredResources.Where(resource => resource is "Z" or "A").Should().Equal("Z", "A");
            tool.StructuredContent!.Value.GetProperty("resolution").GetProperty("resourceOrder")
                .EnumerateArray().Select(item => item.GetString()).Where(resource => resource is "Z" or "A")
                .Should().Equal("Z", "A");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldDeduplicateThirdPartyResourcesInDeclarationOrder()
    {
        var projectRoot = CreateTempProjectWithResourcePack("ordered.resources", "B", "A", "B");
        try
        {
            var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
            var registry = new PackRegistry(
                ComposerPackPaths.BuiltinRoot(repoRoot),
                ComposerPackPaths.ProjectLocalRoot(projectRoot));
            var result = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest("""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "OrderedResources",
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "ordered.resources", "version": "1.0.0", "required": false, "role": "extension" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": { "kind": "wpfui.button" }
                }
                """));

            result.Success.Should().BeTrue();
            result.RequiredResources.Where(resource => resource is "A" or "B")
                .Should().Equal("B", "A");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void FindResourceConflicts_ShouldWarnWhenDeclaredPackCannotBeInspected()
    {
        var missingPack = new PackRegistryItem(
            "broken.resources",
            "1.0.0",
            PackScope.ProjectLocal,
            Path.Combine(Path.GetTempPath(), "missing-pack-" + Guid.NewGuid().ToString("N")),
            0,
            0,
            0,
            0,
            false,
            "",
            []);

        var plan = BlueprintResolutionPlanner.Build(
            new UiBlueprint
            {
                Packs = [new ComposerPackReference { Id = missingPack.Id, Version = missingPack.Version }]
            },
            [missingPack]);

        plan.Conflicts.Should().ContainSingle(issue => issue.Code == "PackResourceInspectionFailed"
            && issue.Message.Contains("broken.resources", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlueprint_ShouldAcceptOptionalSyntaxHighlightPackWithDottedId()
    {
        var projectRoot = CreateTempProjectWithBlockPack(
            "wpfui.syntaxhighlight",
            "wpfui.syntaxhighlight.codeEditor");
        try
        {
            var result = CreateValidator(projectRoot).Validate("""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "OptionalSyntaxHighlight",
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "wpfui.syntaxhighlight", "version": "1.0.0", "required": false, "role": "control-pack" },
                    { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": {
                    "kind": "wpfui.card",
                    "slots": {
                      "content": [
                        {
                          "kind": "core.stack",
                          "slots": {
                            "children": [{ "kind": "wpfui.syntaxhighlight.codeEditor" }]
                          }
                        }
                      ]
                    }
                  }
                }
                """);

            result.Errors.Should().BeEmpty();
            result.Warnings.Should().NotContain(issue => issue.JsonPath == "$.packs[1]"
                && issue.Code == "UnusedPack");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldErrorWhenMissingOptionalDottedPackIsUsed()
    {
        var result = CreateValidator().Validate("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "MissingOptionalSyntaxHighlight",
              "packs": [
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                { "id": "wpfui.syntaxhighlight", "version": "1.0.0", "required": false, "role": "control-pack" },
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" }
              ],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "wpfui.card",
                "slots": {
                  "content": [
                    {
                      "kind": "core.stack",
                      "slots": {
                        "children": [{ "kind": "wpfui.syntaxhighlight.codeEditor" }]
                      }
                    }
                  ]
                }
              }
            }
            """);

        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.slots.content[0].slots.children[0]"
            && issue.Code == "OptionalPackMissing"
            && issue.Message.Contains("wpfui.syntaxhighlight", StringComparison.Ordinal));
        result.Errors.Should().NotContain(issue => issue.Code == "UnknownBlockKind"
            && issue.Message.Contains("wpfui", StringComparison.Ordinal));
    }

    private static BlueprintValidationService CreateValidator(string? projectRoot = null)
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        var projectPackRoot = projectRoot is null ? null : ComposerPackPaths.ProjectLocalRoot(projectRoot);
        return new(new PackRegistry(ComposerPackPaths.BuiltinRoot(repoRoot), projectPackRoot));
    }

    private static string CreateTempProjectWithResourcePack(
        string id,
        params string[] resources)
        => CreateTempProjectWithResourcePacks((id, "control-pack", resources));

    private static string CreateTempProjectWithPackKind(string id, string kind)
        => CreateTempProjectWithResourcePacks((id, kind, Array.Empty<string>()));

    private static string CreateTempProjectWithResourcePacks(
        params (string Id, string Kind, string[] Resources)[] packs)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-pack-conflict-" + Guid.NewGuid().ToString("N"));
        foreach (var (id, kind, resources) in packs)
        {
            var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", id, "1.0.0");
            Directory.CreateDirectory(packRoot);
            File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), $$"""
                {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"{{id}}","version":"1.0.0","scope":"project-local","path":"{{id}}/1.0.0","enabled":true}
                """);
            var resourcesJson = JsonSerializer.Serialize(resources);
            File.WriteAllText(Path.Combine(packRoot, "pack.json"), $$"""
                {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"{{id}}","kind":"{{kind}}","version":"1.0.0","displayName":"Test Resources","resourceSetup":{"applicationMergedDictionaries":{{resourcesJson}}},"blocks":[],"recipes":[]}
                """);
            File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
                {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[]}
                """);
        }

        return projectRoot;
    }

    private static string CreateTempProjectWithBlockPack(string id, string blockKind)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-pack-block-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", id, "1.0.0");
        var blockId = blockKind[(id.Length + 1)..];
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"{{id}}","version":"1.0.0","scope":"project-local","path":"{{id}}/1.0.0","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), $$"""
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"{{id}}","version":"1.0.0","displayName":"Optional Block Pack","resourceSetup":{"applicationMergedDictionaries":[]},"blocks":["{{blockKind}}"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", $"{blockId}.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"$kind$","displayName":"Code Editor","category":"input","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/$blockId$.xaml.sbn"}}
            """.Replace("$kind$", blockKind, StringComparison.Ordinal)
            .Replace("$blockId$", blockId, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", $"{blockId}.xaml.sbn"), "<TextBox />");
        return projectRoot;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
