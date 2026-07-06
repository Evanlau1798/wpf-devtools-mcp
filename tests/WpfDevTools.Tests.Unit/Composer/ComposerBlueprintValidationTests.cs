using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintValidationTests
{
    [Theory]
    [MemberData(nameof(ValidBlueprints))]
    public void ValidateBlueprint_ShouldAcceptBaselineBlueprintShapes(string blueprintJson)
    {
        var validator = CreateValidator();

        var result = validator.Validate(blueprintJson);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateBlueprint_ShouldRequirePrimaryPackRole()
    {
        var validator = CreateValidator();
        var missingRole = Blueprint("""
            {
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true }],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """);
        var wrongRole = Blueprint("""
            {
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "optional" }],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """);

        var missingResult = validator.Validate(missingRole);
        var wrongResult = validator.Validate(wrongRole);

        missingResult.Errors.Should().Contain(issue => issue.JsonPath == "$.packs[0].role"
            && issue.Code == "PrimaryPackRoleMismatch");
        wrongResult.Errors.Should().Contain(issue => issue.JsonPath == "$.packs[0].role"
            && issue.Code == "PrimaryPackRoleMismatch");
    }

    [Fact]
    public void ValidateBlueprint_ShouldRejectUnknownPropertyAndWarnForUnusedDeclaredPack()
    {
        var projectRoot = CreateTempProjectWithValidationPack();
        try
        {
            var validator = CreateValidator(projectRoot);
            var blueprint = Blueprint("""
                {
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "validation", "version": "1.0.0", "required": false, "role": "optional" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": {
                    "kind": "wpfui.button",
                    "properties": { "rawXaml": "<Button />" }
                  }
                }
                """);

            var result = validator.Validate(blueprint);

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.rawXaml"
                && issue.Code == "UnknownProperty");
            result.Warnings.Should().Contain(issue => issue.JsonPath == "$.packs[1]"
                && issue.Code == "UnusedPack");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldRejectUnknownPackAndPrimaryPackOutsidePacks()
    {
        var validator = CreateValidator();
        var blueprint = Blueprint("""
            {
              "packs": [{ "id": "missing", "version": "0.1.0", "required": true }],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.packs[0]"
            && issue.Code == "PackNotFound"
            && issue.RepairSuggestion.Contains("Install or reference", StringComparison.Ordinal));
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.primaryPack"
            && issue.Code == "PrimaryPackNotDeclared");
    }

    [Theory]
    [InlineData("button", "UnqualifiedBlockKind")]
    [InlineData("wpfui.missing", "UnknownBlockKind")]
    [InlineData("unknown.button", "PackNotDeclared")]
    public void ValidateBlueprint_ShouldRejectInvalidBlockKinds(string kind, string code)
    {
        var validator = CreateValidator();
        var blueprint = Blueprint($$"""
            {
              "layout": { "kind": "{{kind}}" }
            }
            """);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout"
            && issue.Code == code
            && !string.IsNullOrWhiteSpace(issue.RepairSuggestion));
    }

    [Fact]
    public void ValidateBlueprint_ShouldRejectExplicitVersionMismatch()
    {
        var validator = CreateValidator();
        var blueprint = Blueprint("""
            {
              "packs": [{ "id": "wpfui", "version": "9.9.9", "required": true }],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button" }
            }
            """);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.packs[0].version"
            && issue.Code == "PackVersionMismatch"
            && issue.RepairSuggestion.Contains("0.1.0", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlueprint_ShouldResolvePackQualifiedKindsAcrossDeclaredPacks()
    {
        var projectRoot = CreateTempProjectWithValidationPack();
        try
        {
            var validator = CreateValidator(projectRoot);
            var qualified = Blueprint("""
                {
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "validation", "version": "1.0.0", "required": true, "role": "optional" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": {
                    "kind": "validation.demo",
                    "properties": { "title": "Secondary pack block" }
                  }
                }
                """);
            var unqualified = Blueprint("""
                {
                  "packs": [
                    { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" },
                    { "id": "validation", "version": "1.0.0", "required": true, "role": "optional" }
                  ],
                  "primaryPack": "wpfui",
                  "layout": {
                    "kind": "demo",
                    "properties": { "title": "Ambiguous block" }
                  }
                }
                """);

            var qualifiedResult = validator.Validate(qualified);
            var unqualifiedResult = validator.Validate(unqualified);

            qualifiedResult.Success.Should().BeTrue("pack-qualified kinds must resolve even when primaryPack points elsewhere");
            unqualifiedResult.Errors.Should().Contain(issue => issue.JsonPath == "$.layout"
                && issue.Code == "UnqualifiedBlockKind");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldReportUnknownSlotWithJsonPath()
    {
        var validator = CreateValidator();
        var blueprint = Blueprint("""
            {
              "layout": {
                "kind": "wpfui.card",
                "slots": {
                  "missing": [{ "kind": "wpfui.button" }]
                }
              }
            }
            """);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.slots.missing"
            && issue.Code == "UnknownSlot"
            && issue.RepairSuggestion.Contains("content", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateBlueprint_ShouldReportForbiddenChildWithAllowedKinds()
    {
        var validator = CreateValidator();
        var blueprint = Blueprint("""
            {
              "layout": {
                "kind": "wpfui.navigationView",
                "slots": {
                  "items": [{ "kind": "wpfui.button" }]
                }
              }
            }
            """);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeFalse();
        var issue = result.Errors.Should().ContainSingle(error => error.Code == "SlotChildKindNotAllowed").Subject;
        issue.JsonPath.Should().Be("$.layout.slots.items[0]");
        issue.ParentSlot.Should().Be("items");
        issue.AllowedKinds.Should().Contain("wpfui.navigationViewItem");
        issue.RepairSuggestion.Should().Contain("wpfui.navigationViewItem");
    }

    [Fact]
    public void ValidateBlueprint_ShouldValidateRequiredEnumAndPropertyTypes()
    {
        var projectRoot = CreateTempProjectWithValidationPack();
        try
        {
            var validator = CreateValidator(projectRoot);
            var blueprint = """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "ValidationDemo",
                  "packs": [{ "id": "validation", "version": "1.0.0", "required": true, "role": "primary" }],
                  "primaryPack": "validation",
                  "layout": {
                    "kind": "validation.demo",
                    "properties": {
                      "count": "wrong",
                      "mode": "invalid",
                      "extra": true
                    }
                  }
                }
                """;

            var result = validator.Validate(blueprint);

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.title"
                && issue.Code == "RequiredPropertyMissing");
            result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.count"
                && issue.Code == "PropertyTypeMismatch");
            var enumIssue = result.Errors.Should().ContainSingle(issue => issue.JsonPath == "$.layout.properties.mode"
                && issue.Code == "PropertyValueNotAllowed").Subject;
            enumIssue.AllowedValues.Should().Equal("compact", "expanded");
            result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.extra"
                && issue.Code == "UnknownProperty");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldAllowTextPrimitiveInTextSlot()
    {
        var validator = CreateValidator();
        var blueprint = Blueprint("""
            {
              "layout": {
                "kind": "wpfui.card",
                "slots": {
                  "content": [{ "kind": "text", "properties": { "value": "Hello" } }]
                }
              }
            }
            """);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateUiBlueprintTool_ShouldReturnStructuredValidationResult()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.ValidateUiBlueprint(
                Blueprint("""
                    {
                      "layout": { "kind": "button" }
                    }
                    """),
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("valid").GetBoolean().Should().BeFalse();
            payload.GetProperty("errorCount").GetInt32().Should().Be(1);
            payload.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("UnqualifiedBlockKind");
            payload.GetProperty("errors")[0].GetProperty("jsonPath").GetString().Should().Be("$.layout");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    public static TheoryData<string> ValidBlueprints()
        => new()
        {
            Blueprint("""
                {
                  "layout": {
                    "kind": "wpfui.fluentWindow",
                    "properties": { "title": "Admin" },
                    "slots": {
                      "titleBar": [{ "kind": "wpfui.titleBar" }],
                      "content": [{
                        "kind": "wpfui.navigationView",
                        "slots": {
                          "items": [{
                            "kind": "wpfui.navigationViewItem",
                            "slots": { "icon": [{ "kind": "wpfui.symbolIcon" }] }
                          }],
                          "content": [{ "kind": "wpfui.card" }]
                        }
                      }]
                    }
                  }
                }
                """),
            Blueprint("""
                {
                  "layout": {
                    "kind": "wpfui.dataGrid",
                    "properties": { "itemsSource": "{Binding Rows}" },
                    "slots": {
                      "emptyState": [{ "kind": "wpfui.textBlock", "properties": { "text": "No rows" } }],
                      "columns": [{ "kind": "template" }]
                    }
                  }
                }
                """),
            Blueprint("""
                {
                  "layout": {
                    "kind": "wpfui.contentDialog",
                    "properties": { "title": "Confirm" },
                    "slots": {
                      "content": [{ "kind": "text", "properties": { "value": "Proceed?" } }],
                      "actions": [{ "kind": "wpfui.button", "properties": { "text": "OK" } }]
                    }
                  }
                }
                """)
        };

    private static BlueprintValidationService CreateValidator(string? projectRoot = null)
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(repoRoot),
            projectRoot is null ? null : ComposerPackPaths.ProjectLocalRoot(projectRoot),
            null);
        return new BlueprintValidationService(registry);
    }

    private static string Blueprint(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var packs = root.TryGetProperty("packs", out var packElement)
            ? packElement.GetRawText()
            : """[{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }]""";
        var primaryPack = root.TryGetProperty("primaryPack", out var primaryElement)
            ? primaryElement.GetRawText()
            : JsonSerializer.Serialize("wpfui");
        var name = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetRawText()
            : JsonSerializer.Serialize("TestBlueprint");
        var layout = root.GetProperty("layout").GetRawText();

        return $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": {{name}},
              "packs": {{packs}},
              "primaryPack": {{primaryPack}},
              "metadata": { "source": "unit-test" },
              "layout": {{layout}}
            }
            """;
    }

    private static string CreateTempProjectWithValidationPack()
    {
        var projectRoot = CreateTempDirectory();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "validation", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"validation","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"validation","displayName":"Validation Pack","version":"1.0.0","blocks":["validation.demo"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "demo.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"validation.demo","displayName":"Demo","category":"test","properties":{"title":{"type":"string","required":true},"count":{"type":"number"},"enabled":{"type":"boolean"},"options":{"type":"object"},"mode":{"type":"string","allowedValues":["compact","expanded"]}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/demo.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "demo.xaml.sbn"), "<TextBlock />");
        return projectRoot;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
