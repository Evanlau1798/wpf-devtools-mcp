using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpPrompts;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpProgressiveDiscoveryBudgetTests
{
    private const int ServerInstructionBudgetChars = 26_000;
    private const int ToolDescriptionBudgetChars = 105_000;
    private const int ToolParameterDescriptionBudgetChars = 27_000;
    private const int OutputSchemaDescriptionBudgetChars = 345_000;
    private static readonly Assembly McpServerAssembly = typeof(ServerInstructions).Assembly;

    [Fact]
    public void InitialInstructionsAndToolDescriptions_ShouldStayWithinProgressiveDiscoveryBudget()
    {
        var descriptions = GetToolDescriptions().ToArray();

        ServerInstructions.Value.Length.Should().BeLessThanOrEqualTo(
            ServerInstructionBudgetChars,
            "long workflows should live in MCP resources or prompts so initialize stays focused on routing");
        descriptions.Sum(description => description.Length).Should().BeLessThanOrEqualTo(
            ToolDescriptionBudgetChars,
            "tool descriptions should be selection-oriented and defer long workflows/examples to resources");
    }

    [Fact]
    public void OutputSchemaDescriptions_ShouldStayWithinProgressiveDiscoveryBudget()
    {
        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();
        var descriptionCharacters = GetToolMethods().Sum(method =>
        {
            var tool = McpServerTool.Create(
                method,
                target: null,
                new McpServerToolCreateOptions { Services = services });
            McpToolOutputSchemas.Apply(tool.ProtocolTool);
            return CountDescriptionCharacters(tool.ProtocolTool.OutputSchema!.Value);
        });

        descriptionCharacters.Should().BeLessThanOrEqualTo(
            OutputSchemaDescriptionBudgetChars,
            "shared output-schema prose should stay compact while fields and structure remain authoritative");
    }

    [Fact]
    public void RecursiveJsonValueBranches_ShouldRelyOnTheirAuthoritativeTypes()
    {
        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();
        var method = GetToolMethods().Single(method =>
            method.GetCustomAttribute<McpServerToolAttribute>()?.Name == "get_active_process");
        var tool = McpServerTool.Create(
            method,
            target: null,
            new McpServerToolCreateOptions { Services = services });
        McpToolOutputSchemas.Apply(tool.ProtocolTool);

        var branches = tool.ProtocolTool.OutputSchema!.Value
            .GetProperty("properties")
            .GetProperty("nextSteps")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("params")
            .GetProperty("additionalProperties")
            .GetProperty("oneOf")
            .EnumerateArray()
            .ToArray();
        var objectBranch = branches.Single(branch => branch.GetProperty("type").GetString() == "object");
        var arrayBranch = branches.Single(branch => branch.GetProperty("type").GetString() == "array");

        objectBranch.TryGetProperty("description", out _).Should().BeFalse();
        objectBranch.TryGetProperty("additionalProperties", out _).Should().BeTrue();
        arrayBranch.TryGetProperty("description", out _).Should().BeFalse();
        arrayBranch.TryGetProperty("items", out _).Should().BeTrue();
    }

    [Fact]
    public void ToolParameterDescriptions_ShouldStayWithinProgressiveDiscoveryBudget()
    {
        var descriptionCharacters = GetToolMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Sum(description => description!.Length);

        descriptionCharacters.Should().BeLessThanOrEqualTo(
            ToolParameterDescriptionBudgetChars,
            "repeated parameter prose should preserve routing details without multiplying context cost");
    }

    [Fact]
    public void StarterPathResource_ShouldExposeMinimumUsefulToolPath()
    {
        var resource = GetResourceByUri("wpf://workflows/starter-path");
        var text = resource.Method.Invoke(null, null).Should().BeOfType<string>().Subject;

        text.Length.Should().BeLessThanOrEqualTo(2_500);
        text.Should().ContainAll(
            "connect",
            "get_ui_summary",
            "get_element_snapshot",
            "get_bindings",
            "get_form_summary",
            "navigation.recommended",
            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true",
            "wpf://contracts/tools");
        ServerInstructions.Value.Should().Contain("wpf://workflows/starter-path");
    }

    [Fact]
    public void StartDiagnosticsPrompt_ShouldExposeMinimumUsefulToolPath()
    {
        var method = typeof(WorkflowPrompts).GetMethod("StartDiagnostics");

        method.Should().NotBeNull("MCP clients should have a portable prompt for the minimum useful diagnostic path");
        var prompt = method!.Invoke(null, null).Should().BeOfType<string>().Subject;

        prompt.Length.Should().BeLessThanOrEqualTo(1_500);
        prompt.Should().ContainAll(
            "minimum useful tool path",
            "WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            "connect()",
            "get_ui_summary",
            "navigation.recommended",
            "get_element_snapshot",
            "wpf://workflows/starter-path");
    }

    [Theory]
    [InlineData("connect", "connect to a running WPF process")]
    [InlineData("get_ui_summary", "scene")]
    [InlineData("get_element_snapshot", "element-centric snapshot")]
    [InlineData("get_bindings", "binding")]
    [InlineData("get_form_summary", "form")]
    public void StarterToolDescriptions_ShouldKeepCriticalSelectionPhrases(
        string toolName,
        string expectedPhrase)
    {
        GetToolDescription(toolName).Should().ContainEquivalentOf(expectedPhrase);
    }

    private static IEnumerable<string> GetToolDescriptions()
        => GetToolMethods()
            .Select(method => method.GetCustomAttribute<DescriptionAttribute>()?.Description)
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Select(description => description!);

    private static string GetToolDescription(string toolName)
        => GetToolMethods()
            .Single(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name == toolName)
            .GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? string.Empty;

    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        foreach (var type in McpServerAssembly.GetTypes()
                     .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() != null)
                {
                    yield return method;
                }
            }
        }
    }

    private static int CountDescriptionCharacters(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Sum(CountDescriptionCharacters);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        return element.EnumerateObject().Sum(property =>
            property.NameEquals("description") && property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()!.Length
                : CountDescriptionCharacters(property.Value));
    }

    private static (MethodInfo Method, McpServerResourceAttribute Attribute) GetResourceByUri(string uriTemplate)
        => GetResourceMethods().Single(resource => resource.Attribute.UriTemplate == uriTemplate);

    private static IEnumerable<(MethodInfo Method, McpServerResourceAttribute Attribute)> GetResourceMethods()
    {
        foreach (var type in McpServerAssembly.GetTypes()
                     .Where(type => type.GetCustomAttribute<McpServerResourceTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerResourceAttribute>();
                if (attribute != null)
                {
                    yield return (method, attribute);
                }
            }
        }
    }
}
