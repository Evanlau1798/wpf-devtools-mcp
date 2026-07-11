using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
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
                { "id": "wpfui", "version": "9.9.9", "required": false, "role": "optional-control" }
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
        var projectRoot = CreateTempProjectWithResourcePack("conflict.resources");
        try
        {
            var result = CreateValidator(projectRoot).Validate("""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "ResourceConflict",
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "conflict.resources", "version": "1.0.0", "required": false, "role": "optional-control" }
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

        var warnings = BlueprintPackConflictDiagnostics.FindResourceConflicts(
            new Dictionary<string, PackRegistryItem>(StringComparer.Ordinal)
            {
                [missingPack.Id] = missingPack
            });

        warnings.Should().ContainSingle(issue => issue.JsonPath == "$.packs"
            && issue.Code == "PackResourceInspectionFailed"
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
                    { "id": "wpfui.syntaxhighlight", "version": "1.0.0", "required": false, "role": "optional-control" },
                    { "id": "core", "version": "0.1.0", "required": true, "role": "optional" }
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
                { "id": "wpfui.syntaxhighlight", "version": "1.0.0", "required": false, "role": "optional-control" },
                { "id": "core", "version": "0.1.0", "required": true, "role": "optional" }
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

    private static string CreateTempProjectWithResourcePack(string id)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-pack-conflict-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", id, "1.0.0");
        Directory.CreateDirectory(packRoot);
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"{{id}}","version":"1.0.0","scope":"project-local","path":"{{id}}/1.0.0","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), $$"""
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"{{id}}","version":"1.0.0","displayName":"Conflict Resources","resourceSetup":{"applicationMergedDictionaries":["{{WpfUiResourceDictionary}}"]},"blocks":[],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[]}
            """);
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
