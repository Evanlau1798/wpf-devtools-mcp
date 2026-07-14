using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintNodeAliasTests
{
    [Fact]
    public async Task PatchUiBlueprintDraft_ShouldResolveNamedNodeAliasAndPublishExactPath()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            Blueprint("ActionRail"),
            CancellationToken.None);
        var sourceRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        using var value = JsonDocument.Parse("12");

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            sourceRef,
            jsonPath: "@ActionRail.properties.spacing",
            value: value.RootElement,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("sourceDraftRef").GetString().Should().Be(sourceRef);
        payload.GetProperty("draftRef").GetString().Should().NotBe(sourceRef);
        var change = payload.GetProperty("changeSummary").GetProperty("changes")[0];
        change.GetProperty("jsonPath").GetString().Should().Be("$.layout.properties.spacing");
        change.GetProperty("after").GetString().Should().Be("12");
    }

    [Fact]
    public async Task PatchUiBlueprintDraft_ShouldRejectUnknownNamedNodeWithoutDerivingDraft()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            Blueprint("ActionRail"),
            CancellationToken.None);
        var sourceRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        using var value = JsonDocument.Parse("12");

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            sourceRef,
            jsonPath: "@MissingRail.properties.spacing",
            value: value.RootElement,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("BlueprintDraftElementNotFound");
        payload.TryGetProperty("draftRef", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PatchUiBlueprintDraft_ShouldRejectAmbiguousNamedNodeWithoutGuessing()
    {
        var created = await UiComposerMcpTools.CreateUiBlueprintDraft(
            """
            {"layout":{"kind":"sample.stack","elementName":"Repeated","properties":{},"slots":{"items":[{"kind":"sample.text","elementName":"Repeated"}]}}}
            """,
            CancellationToken.None);
        var sourceRef = created.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        using var value = JsonDocument.Parse("12");

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            sourceRef,
            jsonPath: "@Repeated.properties.spacing",
            value: value.RootElement,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("errors")[0]
            .GetProperty("code").GetString().Should().Be("BlueprintDraftElementAmbiguous");
    }

    private static string Blueprint(string elementName)
        => JsonSerializer.Serialize(new
        {
            layout = new
            {
                kind = "sample.stack",
                elementName,
                properties = new { },
                slots = new { }
            }
        });
}
