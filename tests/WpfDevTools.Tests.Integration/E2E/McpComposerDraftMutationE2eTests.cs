using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "Integration")]
public sealed class McpComposerDraftMutationE2eTests
{
    [Fact]
    public async Task ComposeBlueprintFailure_ShouldExposeMcpErrorAndStructuredOutcome()
    {
        var isolatedPackRoot = Path.Combine(
            Path.GetTempPath(),
            "wpfdevtools-compose-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedPackRoot);
        using var client = new McpStdioClient();
        try
        {
            await client.StartAsync(FindServerExecutable());

            var envelope = await client.CallToolEnvelopeAsync(
                "compose_ui_blueprint",
                new
                {
                    blueprintJson = "{}",
                    targetPath = "$.layout.slots.content",
                    kind = "missing.block",
                    projectRoot = isolatedPackRoot,
                    localAppDataRoot = isolatedPackRoot
                });

            var result = envelope.GetProperty("result");
            result.GetProperty("isError").GetBoolean().Should().BeTrue(envelope.GetRawText());
            var structured = result.GetProperty("structuredContent");
            structured.GetProperty("success").GetBoolean().Should().BeFalse(envelope.GetRawText());
            structured.GetProperty("composed").GetBoolean().Should().BeFalse(envelope.GetRawText());
            structured.GetProperty("errors")[0].GetProperty("code").GetString()
                .Should().Be("BlockNotComposable");
        }
        finally
        {
            Directory.Delete(isolatedPackRoot, recursive: true);
        }
    }

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

    [Fact]
    public async Task PatchDraft_ShouldBindAtomicOperationsOverStdio()
    {
        using var client = new McpStdioClient();
        await client.StartAsync(FindServerExecutable());
        var created = await client.CallToolAsync(
            "create_ui_blueprint_draft",
            new { blueprintJson = """{"left":"old","right":"old"}""" });
        var draftRef = created.GetProperty("draftRef").GetString()!;

        var patched = await client.CallToolAsync(
            "patch_ui_blueprint_draft",
            new
            {
                draftRef,
                operations = new object[]
                {
                    new { jsonPath = "$.left", value = "new-left" },
                    new { jsonPath = "$.right", value = "new-right" },
                    new { jsonPath = "$.optional", value = (object?)null }
                }
            });

        patched.GetProperty("success").GetBoolean().Should().BeTrue(patched.GetRawText());
        patched.GetProperty("sourceDraftRef").GetString().Should().Be(draftRef);
        patched.GetProperty("changeSummary").GetProperty("changes").EnumerateArray()
            .Select(change => change.GetProperty("jsonPath").GetString())
            .Should().Equal("$.left", "$.right", "$.optional");
        patched.GetProperty("changeSummary").GetProperty("changes")[2]
            .GetProperty("after").GetString().Should().Be("null");
    }

    [Fact]
    public async Task PatchDraft_ShouldRejectNullAtomicOperationWithIndexedStructuredError()
    {
        using var client = new McpStdioClient();
        await client.StartAsync(FindServerExecutable());
        var created = await client.CallToolAsync(
            "create_ui_blueprint_draft",
            new { blueprintJson = """{"name":"old"}""" });
        var draftRef = created.GetProperty("draftRef").GetString()!;

        var envelope = await client.CallToolEnvelopeAsync(
            "patch_ui_blueprint_draft",
            new { draftRef, operations = new object?[] { null } });

        var result = envelope.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue(envelope.GetRawText());
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("success").GetBoolean().Should().BeFalse(envelope.GetRawText());
        var error = structured.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("BlueprintDraftOperationRequired");
        error.GetProperty("jsonPath").GetString().Should().Be("$.operations[0]");
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
