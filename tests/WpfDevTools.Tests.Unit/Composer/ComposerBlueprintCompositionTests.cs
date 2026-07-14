using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintCompositionTests
{
    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldInsertPackSkeletonAtNestedSlotPath()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        try
        {
            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                CreateBlueprint(),
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.action",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeTrue(payload.GetRawText());
            payload.GetProperty("insertedPath").GetString()
                .Should().Be("$.layout.slots.content[0].slots.items[0]");
            payload.GetProperty("validation").GetProperty("valid").GetBoolean().Should().BeTrue(payload.GetRawText());
            payload.GetProperty("observability").GetProperty("privacy")
                .GetProperty("absoluteLocalPathsIncluded").GetBoolean().Should().BeFalse();

            var inserted = payload.GetProperty("blueprint")
                .GetProperty("layout").GetProperty("slots").GetProperty("content")[0]
                .GetProperty("slots").GetProperty("items")[0];
            inserted.GetProperty("kind").GetString().Should().Be("nebula.action");
            inserted.GetProperty("properties").GetProperty("label").GetString().Should().Be("Execute");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldApplyPackValidatedPropertiesDuringInsertion()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        using var properties = JsonDocument.Parse("""{"label":"Run now"}""");
        try
        {
            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                CreateBlueprint(),
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.action",
                properties: properties.RootElement,
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("composed").GetBoolean().Should().BeTrue(payload.GetRawText());
            payload.GetProperty("blueprint")
                .GetProperty("layout").GetProperty("slots").GetProperty("content")[0]
                .GetProperty("slots").GetProperty("items")[0]
                .GetProperty("properties").GetProperty("label").GetString().Should().Be("Run now");
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldRejectNonObjectPropertiesWithoutCreatingACandidate()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        using var properties = JsonDocument.Parse("[]");
        try
        {
            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                CreateBlueprint(),
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.action",
                properties: properties.RootElement,
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            var payload = result.StructuredContent!.Value;
            result.IsError.Should().BeTrue();
            payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("errors")[0].GetProperty("code").GetString()
                .Should().Be("InvalidCompositionProperties");
            payload.TryGetProperty("candidateBlueprintJson", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("$.layout.slots.content.slots.items")]
    [InlineData("$.layout.slots.content[999999999999999999999999999999].slots.items")]
    public async Task ComposeUiBlueprintTool_ShouldRejectAmbiguousOrInvalidTargetPaths(string targetPath)
    {
        var projectRoot = CreateProjectWithCompositionPack();
        try
        {
            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                CreateBlueprint(),
                targetPath: targetPath,
                kind: "nebula.action",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("errors")[0].GetProperty("code").GetString()
                .Should().Be("InvalidCompositionTargetPath");
            payload.TryGetProperty("blueprint", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldReturnPackValidationForForbiddenChild()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        try
        {
            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                CreateBlueprint(),
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.frame",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            var payload = result.StructuredContent!.Value;
            result.IsError.Should().BeTrue();
            payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("validation").GetProperty("errors")
                .EnumerateArray().Select(error => error.GetProperty("code").GetString())
                .Should().Contain("SlotChildKindNotAllowed");
            payload.GetProperty("candidateWritten").GetBoolean().Should().BeFalse();
            payload.GetProperty("invalidCandidate").GetProperty("layout")
                .GetProperty("slots").GetProperty("content")[0]
                .GetProperty("slots").GetProperty("items")[0]
                .GetProperty("kind").GetString().Should().Be("nebula.frame");
            JsonDocument.Parse(payload.GetProperty("candidateBlueprintJson").GetString()!).RootElement
                .GetProperty("layout").GetProperty("kind").GetString().Should().Be("nebula.frame");
            payload.TryGetProperty("blueprint", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldPreserveDraftCandidateRecoveryOnCompositionError()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        try
        {
            var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
                CreateBlueprint(),
                CancellationToken.None);
            var sourceDraftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;

            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                sourceDraftRef,
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.frame",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeTrue();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("sourceDraftRef").GetString().Should().Be(sourceDraftRef);
            payload.GetProperty("candidateDraftCreated").GetBoolean().Should().BeTrue();
            payload.GetProperty("candidateWritten").GetBoolean().Should().BeFalse();
            payload.GetProperty("candidateDraftRef").GetString().Should().NotBeNullOrWhiteSpace();
            payload.GetProperty("validation").GetProperty("valid").GetBoolean().Should().BeFalse();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldRejectRawCandidateThatExceedsTheReusableInputLimit()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        try
        {
            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                CreateMaximumLengthBlueprint(),
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.action",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            var payload = result.StructuredContent!.Value;
            result.IsError.Should().BeTrue();
            payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("errors")[0].GetProperty("code").GetString()
                .Should().Be("BlueprintCompositionTooLarge");
            payload.TryGetProperty("candidateBlueprintJson", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ComposeUiBlueprintTool_ShouldRejectDraftCandidateThatExceedsTheReusableInputLimit()
    {
        var projectRoot = CreateProjectWithCompositionPack();
        try
        {
            var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
                CreateMaximumLengthBlueprint(),
                CancellationToken.None);
            var draftRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;

            var result = await UiComposerMcpTools.ComposeUiBlueprint(
                draftRef,
                targetPath: "$.layout.slots.content[0].slots.items",
                kind: "nebula.action",
                projectRoot: projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            var payload = result.StructuredContent!.Value;
            result.IsError.Should().BeTrue();
            payload.GetProperty("success").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("composed").GetBoolean().Should().BeFalse(payload.GetRawText());
            payload.GetProperty("errors")[0].GetProperty("code").GetString()
                .Should().Be("BlueprintCompositionTooLarge");
            payload.TryGetProperty("candidateDraftRef", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private static string CreateBlueprint()
        => JsonSerializer.Serialize(new
        {
            schemaVersion = "wpfdevtools.ui-blueprint.v1",
            name = "NebulaWorkspace",
            packs = new[] { new { id = "nebula", version = "1.0.0", required = true, role = "primary" } },
            primaryPack = "nebula",
            layout = new
            {
                kind = "nebula.frame",
                slots = new
                {
                    content = new[]
                    {
                        new { kind = "nebula.stack", slots = new { items = Array.Empty<object>() } }
                    }
                }
            }
        });

    private static string CreateMaximumLengthBlueprint()
    {
        var blueprint = JsonNode.Parse(CreateBlueprint())!.AsObject();
        blueprint["metadata"] = new JsonObject { ["padding"] = string.Empty };
        var unpaddedLength = blueprint.ToJsonString().Length;
        blueprint["metadata"]!["padding"] = new string(
            'x',
            BoundaryStringLimits.MaxStringifiedJsonArgumentLength - unpaddedLength);
        var result = blueprint.ToJsonString();
        result.Length.Should().Be(BoundaryStringLimits.MaxStringifiedJsonArgumentLength);
        return result;
    }

    private static string CreateProjectWithCompositionPack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-compose-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(projectRoot, ".wpfdevtools", "packs", "nebula", "1.0.0");
        Directory.CreateDirectory(Path.Combine(root, "blocks"));
        Directory.CreateDirectory(Path.Combine(root, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(root, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"nebula","displayName":"Nebula","version":"1.0.0","kind":"control-pack","blocks":["nebula.frame","nebula.stack","nebula.action"],"recipes":[],"xmlNamespaces":{"nebula":"urn:nebula"}}""");
        File.WriteAllText(Path.Combine(root, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Nebula","url":"https://example.invalid/nebula","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        WriteBlock(root, "frame", """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.frame","displayName":"Frame","category":"window","properties":{},"slots":{"content":{"allowedKinds":["nebula.stack"]}},"renderer":{"xamlTemplate":"renderers/xaml/frame.xaml.sbn"},"sourceHints":[]}""");
        WriteBlock(root, "stack", """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.stack","displayName":"Stack","category":"layout","properties":{},"slots":{"items":{"allowedKinds":["nebula.action"]}},"renderer":{"xamlTemplate":"renderers/xaml/stack.xaml.sbn"},"sourceHints":[]}""");
        WriteBlock(root, "action", """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"nebula.action","displayName":"Action","category":"input","properties":{"label":{"type":"string","required":true,"default":"Execute"}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/action.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "frame.xaml.sbn"), "<nebula:Frame>{{slot.content}}</nebula:Frame>");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "stack.xaml.sbn"), "<StackPanel>{{slot.items}}</StackPanel>");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "action.xaml.sbn"), "<nebula:Action Label=\"{{label}}\" />");
        var escapedRoot = root.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(root, "install.manifest.json"),
            $$"""{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"nebula","version":"1.0.0","scope":"project-local","path":"{{escapedRoot}}","enabled":true}""");
        return projectRoot;
    }

    private static void WriteBlock(string root, string name, string json)
        => File.WriteAllText(Path.Combine(root, "blocks", name + ".block.json"), json);
}
