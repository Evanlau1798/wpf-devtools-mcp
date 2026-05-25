using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpPromptAndResourceAttributeTests
{
    private static readonly Assembly McpServerAssembly = typeof(ServerInstructions).Assembly;

    private static readonly List<(Type Type, MethodInfo Method, McpServerPromptAttribute Attr)> AllPrompts =
        GetAllPromptMethods().ToList();

    private static readonly List<(Type Type, MethodInfo Method, McpServerResourceAttribute Attr)> AllResources =
        GetAllResourceMethods().ToList();

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerPromptAttribute Attr)> GetAllPromptMethods()
    {
        var promptTypes = McpServerAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerPromptTypeAttribute>() != null);

        foreach (var type in promptTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerPromptAttribute>();
                if (attr != null)
                {
                    yield return (type, method, attr);
                }
            }
        }
    }

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerResourceAttribute Attr)> GetAllResourceMethods()
    {
        var resourceTypes = McpServerAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerResourceTypeAttribute>() != null);

        foreach (var type in resourceTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerResourceAttribute>();
                if (attr != null)
                {
                    yield return (type, method, attr);
                }
            }
        }
    }

    [Fact]
    public void PromptTypes_ShouldExist()
    {
        AllPrompts.Should().NotBeEmpty(
            "Claude Code discovery should expose workflow prompts as first-class MCP prompts");
    }

    [Fact]
    public void ResourceTypes_ShouldExist()
    {
        AllResources.Should().NotBeEmpty(
            "Claude Code discovery should expose capability and limitation resources as first-class MCP resources");
    }

    [Fact]
    public void AllPrompts_ShouldHaveFriendlyMetadata()
    {
        foreach (var (type, method, attr) in AllPrompts)
        {
            attr.Name.Should().NotBeNullOrWhiteSpace(
                $"prompt '{method.Name}' in {type.Name} should have a stable MCP name");
            attr.Title.Should().NotBeNullOrWhiteSpace(
                $"prompt '{attr.Name}' should have a human-friendly title for slash-command discovery");

            var description = method.GetCustomAttribute<DescriptionAttribute>();
            description.Should().NotBeNull(
                $"prompt '{attr.Name}' should describe its workflow entry point");
            description!.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void AllResources_ShouldHaveFriendlyMetadata()
    {
        foreach (var (type, method, attr) in AllResources)
        {
            attr.Name.Should().NotBeNullOrWhiteSpace(
                $"resource '{method.Name}' in {type.Name} should have a stable MCP name");
            attr.Title.Should().NotBeNullOrWhiteSpace(
                $"resource '{attr.Name}' should have a human-friendly title");
            attr.UriTemplate.Should().NotBeNullOrWhiteSpace(
                $"resource '{attr.Name}' should declare an explicit URI template for @resource discovery");

            var description = method.GetCustomAttribute<DescriptionAttribute>();
            description.Should().NotBeNull(
                $"resource '{attr.Name}' should describe what runtime data it exposes");
            description!.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("connect_and_list_windows")]
    [InlineData("debug_binding_issue")]
    [InlineData("debug_command_or_click")]
    [InlineData("diagnose_elevated_target")]
    public void RequiredWorkflowPrompts_ShouldExist(string promptName)
    {
        AllPrompts.Should().Contain(x => x.Attr.Name == promptName);
    }

    [Theory]
    [InlineData("wpf_capabilities", "wpf://capabilities")]
    [InlineData("wpf_response_contract", "wpf://contracts/response")]
    [InlineData("wpf_starter_path", "wpf://workflows/starter-path")]
    [InlineData("wpf_binding_workflow", "wpf://workflows/binding-debug")]
    [InlineData("wpf_elevated_target_limitations", "wpf://limitations/elevated-targets")]
    [InlineData("wpf_injection_failure_limitations", "wpf://limitations/injection-failures")]
    [InlineData("wpf_window_focus_limitations", "wpf://limitations/window-focus")]
    [InlineData("wpf_performance_profiling_notes", "wpf://limitations/performance-profiling")]
    [InlineData("wpf_state_safety_notes", "wpf://limitations/state-safety")]
    [InlineData("wpf_screenshot_png", "wpf://screenshots/{screenshotId}")]
    public void RequiredRuntimeResources_ShouldExist(string resourceName, string uriTemplate)
    {
        AllResources.Should().Contain(x =>
            x.Attr.Name == resourceName &&
            x.Attr.UriTemplate == uriTemplate);
    }
}
