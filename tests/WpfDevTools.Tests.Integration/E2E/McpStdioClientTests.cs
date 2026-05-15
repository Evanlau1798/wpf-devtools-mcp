using System.Reflection;
using System.Text.Json;
using FluentAssertions;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class McpStdioClientTests
{
  [Fact]
  public void CreateProcessEnvironment_WhenTempRootMissing_ShouldInjectIsolatedTempOverrides()
  {
    var environment = McpStdioClient.CreateProcessEnvironment(
      new Dictionary<string, string>
      {
        ["WPFDEVTOOLS_AUTH_SECRET"] = "secret"
      },
      @"C:\temp\mcp-client-isolated");

    environment["WPFDEVTOOLS_AUTH_SECRET"].Should().Be("secret");
    environment["TEMP"].Should().Be(@"C:\temp\mcp-client-isolated");
    environment["TMP"].Should().Be(@"C:\temp\mcp-client-isolated");
  }

  [Fact]
  public void CreateProcessEnvironment_WhenCallerProvidesTempRoot_ShouldPreserveExplicitOverrides()
  {
    var environment = McpStdioClient.CreateProcessEnvironment(
      new Dictionary<string, string>
      {
        ["TEMP"] = @"C:\temp\explicit-root"
      },
      @"C:\temp\fallback-root");

    environment["TEMP"].Should().Be(@"C:\temp\explicit-root");
    environment["TMP"].Should().Be(@"C:\temp\explicit-root");
  }

  [Fact]
  public void CreateProcessEnvironment_WhenTempKeyUsesDifferentCase_ShouldTreatItAsExplicitOverride()
  {
    var environment = McpStdioClient.CreateProcessEnvironment(
      new Dictionary<string, string>
      {
        ["temp"] = @"C:\temp\mixed-case-root"
      },
      @"C:\temp\fallback-root");

    environment["TEMP"].Should().Be(@"C:\temp\mixed-case-root");
    environment["TMP"].Should().Be(@"C:\temp\mixed-case-root");
  }

  [Fact]
  public void CreateProcessEnvironment_WhenTempAndTmpAreDistinct_ShouldPreserveBothExplicitValues()
  {
    var environment = McpStdioClient.CreateProcessEnvironment(
      new Dictionary<string, string>
      {
        ["TEMP"] = @"C:\temp\primary-root",
        ["TMP"] = @"D:\scratch\secondary-root"
      },
      @"C:\temp\fallback-root");

    environment["TEMP"].Should().Be(@"C:\temp\primary-root");
    environment["TMP"].Should().Be(@"D:\scratch\secondary-root");
  }

    [Fact]
    public void ExtractToolResult_ShouldPreferStructuredContent_WhenAvailable()
    {
        var response = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "result": {
                "content": [
                  {
                    "type": "text",
                    "text": "{\"success\":true,\"nodes\":[{\"elementId\":\"Button_1\"}]}"
                  }
                ],
                "structuredContent": {
                  "success": true,
                  "summaryText": "- Button SaveButton"
                }
              }
            }
            """);

        var method = typeof(McpStdioClient).GetMethod(
            "ExtractToolResult",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var result = (JsonElement)method!.Invoke(null, [response])!;

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("summaryText").GetString().Should().Be("- Button SaveButton");
        result.TryGetProperty("nodes", out _).Should().BeFalse();
    }
}
