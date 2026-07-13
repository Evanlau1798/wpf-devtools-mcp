using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "Integration")]
public sealed class McpComposerDraftMutationE2eTests
{
    [Fact]
    public async Task PatchDraft_ShouldInterpretExplicitNullDefaultsByMutationMode()
    {
        using var client = new McpStdioClient();
        await client.StartAsync(FindServerExecutable());
        var created = await client.CallToolAsync(
            "create_ui_blueprint_draft",
            new { blueprintJson = """{"name":"old","removeMe":true}""" });
        var draftRef = created.GetProperty("draftRef").GetString()!;

        var merged = await client.CallToolAsync(
            "patch_ui_blueprint_draft",
            new
            {
                draftRef,
                patchJson = """{"name":"new"}""",
                jsonPath = (string?)null,
                value = (object?)null,
                remove = false
            });
        var removed = await client.CallToolAsync(
            "patch_ui_blueprint_draft",
            new
            {
                draftRef,
                patchJson = (string?)null,
                jsonPath = "$.removeMe",
                value = (object?)null,
                remove = true
            });
        var setToNull = await client.CallToolAsync(
            "patch_ui_blueprint_draft",
            new
            {
                draftRef,
                patchJson = (string?)null,
                jsonPath = "$.optional",
                value = (object?)null,
                remove = false
            });

        merged.GetProperty("success").GetBoolean().Should().BeTrue(merged.GetRawText());
        removed.GetProperty("success").GetBoolean().Should().BeTrue(removed.GetRawText());
        setToNull.GetProperty("success").GetBoolean().Should().BeTrue(setToNull.GetRawText());
        setToNull.GetProperty("changeSummary").GetProperty("changes")[0]
            .GetProperty("after").GetString().Should().Be("null");
    }

    private static string FindServerExecutable()
        => IntegrationExecutableLocator.FindExecutable(
               AppContext.BaseDirectory,
               "src",
               "WpfDevTools.Mcp.Server",
               "net8.0",
               "WpfDevTools.Mcp.Server.exe")
           ?? throw new InvalidOperationException(
               "WpfDevTools.Mcp.Server.exe was not found. Build the MCP server first.");
}
