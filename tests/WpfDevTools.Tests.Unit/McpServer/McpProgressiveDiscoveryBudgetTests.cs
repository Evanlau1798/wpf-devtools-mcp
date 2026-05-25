using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpProgressiveDiscoveryBudgetTests
{
    private const int ServerInstructionBudgetChars = 26_000;
    private const int ToolDescriptionBudgetChars = 120_000;
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
            "WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true",
            "wpf://contracts/tools");
        ServerInstructions.Value.Should().Contain("wpf://workflows/starter-path");
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
