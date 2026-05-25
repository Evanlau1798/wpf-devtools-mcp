using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ToolInputExamplesResourceTests
{
    private static readonly string[] RequiredHighRiskTools =
    [
        "batch_mutate",
        "wait_for_dp_change_after_mutation",
        "element_screenshot",
        "capture_state_snapshot",
        "restore_state_snapshot"
    ];

    [Fact]
    public void ToolExamplesResource_ShouldExposeHighRiskToolExamples()
    {
        using var document = ReadToolExamples();
        var root = document.RootElement;
        var examplesByTool = root.GetProperty("examplesByTool");

        root.GetProperty("resourceUri").GetString().Should().Be("wpf://contracts/tool-examples");
        root.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();

        foreach (var toolName in RequiredHighRiskTools)
        {
            examplesByTool.TryGetProperty(toolName, out var examples).Should().BeTrue(
                $"{toolName} should have machine-readable input examples");
            examples.ValueKind.Should().Be(JsonValueKind.Array);
            examples.GetArrayLength().Should().BeInRange(1, 5);

            foreach (var example in examples.EnumerateArray())
            {
                example.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
                example.GetProperty("arguments").ValueKind.Should().Be(JsonValueKind.Object);
                JsonSerializer.Serialize(example).Should().NotContainAny(
                    "password",
                    "secret",
                    "token",
                    "apiKey",
                    "auth");
            }
        }
    }

    [Fact]
    public void ToolExamplesResource_ShouldDescribeScreenshotResourceFollowUp()
    {
        using var document = ReadToolExamples();
        var screenshotExamples = document.RootElement
            .GetProperty("examplesByTool")
            .GetProperty("element_screenshot");

        screenshotExamples.EnumerateArray().Any(HasScreenshotResourceFollowUp)
            .Should().BeTrue();
    }

    [Fact]
    public void ToolExamplesResource_ShouldOnlyReferenceRegisteredTools()
    {
        using var document = ReadToolExamples();
        var registeredTools = GetRegisteredToolNames();
        var exampleToolNames = document.RootElement
            .GetProperty("examplesByTool")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        exampleToolNames.Should().OnlyContain(name => registeredTools.Contains(name));
    }

    [Fact]
    public void ServerInstructions_ShouldReferenceToolExamplesResource()
    {
        ServerInstructions.Value.Should().Contain("wpf://contracts/tool-examples");
    }

    private static JsonDocument ReadToolExamples()
    {
        var resource = GetResourceByUri("wpf://contracts/tool-examples");
        var json = resource.Method.Invoke(null, null).Should().BeOfType<string>().Subject;
        return JsonDocument.Parse(json);
    }

    private static (MethodInfo Method, McpServerResourceAttribute Attribute) GetResourceByUri(string uriTemplate)
        => typeof(CapabilityResources).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerResourceTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerResourceAttribute>()))
                .Where(resource => resource.Attribute != null)
                .Select(resource => (resource.Method, Attribute: resource.Attribute!)))
            .Single(resource => resource.Attribute.UriTemplate == uriTemplate);

    private static bool HasScreenshotResourceFollowUp(JsonElement example)
        => example.TryGetProperty("resourceFollowUp", out var followUp)
           && followUp.GetProperty("resourceUriTemplate").GetString() == "wpf://screenshots/{screenshotId}";

    private static HashSet<string> GetRegisteredToolNames()
        => typeof(CapabilityResources).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
}
