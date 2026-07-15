using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Composer.Drafts;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintBatchPatchTests
{
    [Fact]
    public async Task PatchDraft_OperationsShouldCreateOneImmutableDraftWithPerPathSummaries()
    {
        var originalRef = await CreateDraftAsync("""{"left":"old","right":"old"}""");

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            originalRef,
            operations:
            [
                new BlueprintDraftPathOperation("$.left", Value("new-left")),
                new BlueprintDraftPathOperation("$.right", Value("new-right"))
            ],
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse(result.StructuredContent?.GetRawText());
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("sourceDraftRef").GetString().Should().Be(originalRef);
        var derivedRef = payload.GetProperty("draftRef").GetString()!;
        derivedRef.Should().NotBe(originalRef);
        payload.GetProperty("changeSummary").GetProperty("changes").EnumerateArray()
            .Select(change => change.GetProperty("jsonPath").GetString())
            .Should().Equal("$.left", "$.right");

        BlueprintInputResolver.Store.Resolve(originalRef).BlueprintJson.Should()
            .Be("""{"left":"old","right":"old"}""");
        BlueprintInputResolver.Store.Resolve(derivedRef).BlueprintJson.Should()
            .Contain("\"left\":\"new-left\"").And.Contain("\"right\":\"new-right\"");
    }

    [Fact]
    public async Task PatchDraft_OperationsShouldApplyInOrder()
    {
        var originalRef = await CreateDraftAsync("""{"name":"ordered"}""");

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            originalRef,
            operations:
            [
                new BlueprintDraftPathOperation("$.settings", Json("{}")),
                new BlueprintDraftPathOperation("$.settings.accent", Value("magenta"))
            ],
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse(result.StructuredContent?.GetRawText());
        var derivedRef = result.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
        BlueprintInputResolver.Store.Resolve(derivedRef).BlueprintJson.Should()
            .Contain("\"settings\":{\"accent\":\"magenta\"}");
    }

    [Fact]
    public async Task PatchDraft_WhenAnyOperationFails_ShouldNotDeriveAPartialDraft()
    {
        var originalRef = await CreateDraftAsync("""{"left":"old"}""");

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            originalRef,
            operations:
            [
                new BlueprintDraftPathOperation("$.left", Value("changed")),
                new BlueprintDraftPathOperation("$.missing", Remove: true)
            ],
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftPathNotFound");
        error.GetProperty("jsonPath").GetString().Should().Be("$.operations[1].jsonPath");
        result.StructuredContent.Value.TryGetProperty("draftRef", out _).Should().BeFalse();
        BlueprintInputResolver.Store.Resolve(originalRef).BlueprintJson.Should()
            .Be("""{"left":"old"}""");
    }

    [Fact]
    public async Task PatchDraft_ShouldRejectMoreThanSixteenOperations()
    {
        var originalRef = await CreateDraftAsync("""{"name":"bounded"}""");
        var operations = Enumerable.Range(0, 17)
            .Select(index => new BlueprintDraftPathOperation($"$.value{index}", Json(index.ToString())))
            .ToArray();

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            originalRef,
            operations: operations,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftTooManyOperations");
        error.GetProperty("jsonPath").GetString().Should().Be("$.operations");
    }

    [Fact]
    public async Task PatchDraft_WhenOperationsExceedTheDraftLimit_ShouldIdentifyTheBatch()
    {
        var originalRef = await CreateDraftAsync(
            JsonSerializer.Serialize(new { padding = new string('x', 60_000) }));

        var result = await UiComposerMcpTools.PatchUiBlueprintDraft(
            originalRef,
            operations: [new BlueprintDraftPathOperation("$.extra", Value(new string('y', 8_000)))],
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftTooLarge");
        error.GetProperty("jsonPath").GetString().Should().Be("$.operations");
    }

    [Fact]
    public void PatchDraft_OperationsShouldPublishABoundedTypedInputSchema()
    {
        var method = typeof(UiComposerMcpTools).GetMethod(nameof(UiComposerMcpTools.PatchUiBlueprintDraft))!;
        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema test does not invoke tools."))
            .BuildServiceProvider();
        var schema = McpServerTool.Create(
            method,
            target: null,
            new McpServerToolCreateOptions { Services = services }).ProtocolTool.InputSchema;

        var operations = schema.GetProperty("properties").GetProperty("operations");
        operations.GetProperty("maxItems").GetInt32().Should().Be(16);
        var item = operations.GetProperty("items");
        item.GetProperty("properties").EnumerateObject().Select(property => property.Name)
            .Should().Contain(["jsonPath", "value", "remove"]);
        item.GetProperty("required").EnumerateArray().Select(value => value.GetString())
            .Should().Contain("jsonPath");
    }

    private static async Task<string> CreateDraftAsync(string json)
    {
        var result = await UiComposerMcpTools.CreateUiBlueprintDraft(json, CancellationToken.None);
        return result.StructuredContent!.Value.GetProperty("draftRef").GetString()!;
    }

    private static JsonElement Value(string value)
        => Json(JsonSerializer.Serialize(value));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
