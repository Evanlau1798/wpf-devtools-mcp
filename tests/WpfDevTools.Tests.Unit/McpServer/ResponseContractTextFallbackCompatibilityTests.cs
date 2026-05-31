using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractTextFallbackCompatibilityTests
{
    [Fact]
    public void ResponseContractResource_ShouldStateStructuredContentIsTheOnlyFullFidelityPayload()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());

        var toolCallResult = document.RootElement.GetProperty("toolCallResult");
        toolCallResult.GetProperty("fullFidelityPayloadField").GetString()
            .Should().Be("result.structuredContent");
        toolCallResult.GetProperty("textFallbackFidelity").GetString()
            .Should().Be("lossy-compatibility-projection");
        toolCallResult.GetProperty("textFallbackFullModeIsFullFidelity").GetBoolean()
            .Should().BeFalse();
        toolCallResult.GetProperty("textFallbackModeEnvironmentVariable").GetString()
            .Should().Be(McpServerConfiguration.TextFallbackModeEnvVar);

        AssertArrayContains(
            toolCallResult.GetProperty("textFallbackFullModeOmittedFieldFamilies"),
            "base64-images",
            "raw-xaml-or-markup",
            "logs-and-traces");

        toolCallResult.GetProperty("olderTextOnlyClientGuidance").GetString()
            .Should().Contain("result.structuredContent")
            .And.Contain("WPFDEVTOOLS_TEXT_FALLBACK_MODE=full")
            .And.Contain("large or sensitive fields remain omitted");
    }

    private static void AssertArrayContains(JsonElement arrayElement, params string[] expectedValues)
    {
        var values = arrayElement
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();

        foreach (var expectedValue in expectedValues)
        {
            values.Should().Contain(expectedValue);
        }
    }
}
