using System.ComponentModel;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Validates that the MCP tools are correctly registered with
/// proper attributes, descriptions, and metadata via the SDK's
/// [McpServerToolType] / [McpServerTool] attribute system.
/// </summary>
public class McpToolAttributeTests
{
    private static readonly Assembly McpServerAssembly =
        typeof(WpfDevTools.Mcp.Server.ServerInstructions).Assembly;

    private static readonly List<(Type Type, MethodInfo Method, McpServerToolAttribute Attr)> AllTools =
        GetAllToolMethods().ToList();

    private static IEnumerable<(Type Type, MethodInfo Method, McpServerToolAttribute Attr)> GetAllToolMethods()
    {
        var toolTypes = McpServerAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr != null)
                {
                    yield return (type, method, attr);
                }
            }
        }
    }

    [Fact]
    public void AllTools_ShouldHaveUniqueNames()
    {
        var names = AllTools.Select(t => t.Attr.Name).ToList();
        names.Should().OnlyHaveUniqueItems("tool names must be unique");
    }

    [Fact]
    public void AllTools_ShouldHaveDescriptionAttribute()
    {
        foreach (var (type, method, attr) in AllTools)
        {
            var desc = method.GetCustomAttribute<DescriptionAttribute>();
            desc.Should().NotBeNull(
                $"tool '{attr.Name}' in {type.Name} must have [Description]");
            desc!.Description.Should().NotBeNullOrWhiteSpace(
                $"tool '{attr.Name}' description must not be empty");
        }
    }

    [Fact]
    public void AllToolParameters_ShouldHaveDescriptionAttribute_WhenExposedToMcpClients()
    {
        foreach (var (type, method, attr) in AllTools)
        {
            var exposedParameters = method.GetParameters()
                .Where(parameter => parameter.ParameterType != typeof(SessionManager))
                .Where(parameter => parameter.ParameterType != typeof(CancellationToken));

            foreach (var parameter in exposedParameters)
            {
                var description = parameter.GetCustomAttribute<DescriptionAttribute>();
                description.Should().NotBeNull(
                    $"tool '{attr.Name}' in {type.Name} must annotate parameter '{parameter.Name}' with [Description] for MCP schema generation");
                description!.Description.Should().NotBeNullOrWhiteSpace(
                    $"tool '{attr.Name}' parameter '{parameter.Name}' description must not be empty");
            }
        }
    }

    [Fact]
    public void AllTools_ShouldExplicitlyDisableOpenWorldMetadata()
    {
        foreach (var (type, _, attr) in AllTools)
        {
            attr.OpenWorld.Should().BeFalse(
                $"tool '{attr.Name}' in {type.Name} should be marked closed-world because it depends on local WPF processes and IPC state");
        }
    }

    [Fact]
    public void AllTools_ShouldHaveNonEmptyName()
    {
        foreach (var (_, _, attr) in AllTools)
        {
            attr.Name.Should().NotBeNullOrWhiteSpace("every tool must have a Name");
        }
    }

    [Theory]
    [InlineData("get_processes")]
    [InlineData("connect")]
    [InlineData("ping")]
    public void ProcessTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_visual_tree")]
    [InlineData("get_logical_tree")]
    [InlineData("serialize_to_xaml")]
    [InlineData("get_namescope")]
    [InlineData("get_template_tree")]
    [InlineData("compare_trees")]
    public void TreeTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_bindings")]
    [InlineData("get_binding_errors")]
    [InlineData("get_binding_value_chain")]
    [InlineData("get_datacontext_chain")]
    [InlineData("force_binding_update")]
    public void BindingTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_dp_value_source")]
    [InlineData("get_dp_metadata")]
    [InlineData("set_dp_value")]
    [InlineData("clear_dp_value")]
    [InlineData("watch_dp_changes")]
    public void DependencyPropertyTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_applied_styles")]
    [InlineData("get_triggers")]
    [InlineData("get_resource_chain")]
    [InlineData("override_style_setter")]
    public void StyleTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("trace_routed_events")]
    [InlineData("get_event_handlers")]
    [InlineData("fire_routed_event")]
    public void EventTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("click_element")]
    [InlineData("drag_and_drop")]
    [InlineData("scroll_to_element")]
    [InlineData("simulate_keyboard")]
    [InlineData("element_screenshot")]
    public void InteractionTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_layout_info")]
    [InlineData("get_clipping_info")]
    [InlineData("highlight_element")]
    [InlineData("invalidate_layout")]
    public void LayoutTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_viewmodel")]
    [InlineData("get_commands")]
    [InlineData("execute_command")]
    [InlineData("modify_viewmodel")]
    [InlineData("get_validation_errors")]
    public void MvvmTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_render_stats")]
    [InlineData("find_binding_leaks")]
    [InlineData("measure_element_render_time")]
    [InlineData("get_visual_count")]
    public void PerformanceTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("get_processes")]
    [InlineData("get_visual_tree")]
    [InlineData("get_logical_tree")]
    [InlineData("get_bindings")]
    [InlineData("get_binding_errors")]
    [InlineData("get_dp_value_source")]
    [InlineData("get_applied_styles")]
    [InlineData("get_event_handlers")]
    [InlineData("element_screenshot")]
    [InlineData("get_layout_info")]
    [InlineData("get_viewmodel")]
    [InlineData("get_render_stats")]
    public void ReadOnlyTools_ShouldBeMarkedReadOnly(string toolName)
    {
        var tool = AllTools.First(t => t.Attr.Name == toolName);
        tool.Attr.ReadOnly.Should().BeTrue(
            $"'{toolName}' is a read-only inspection tool");
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("set_dp_value")]
    [InlineData("clear_dp_value")]
    [InlineData("click_element")]
    [InlineData("fire_routed_event")]
    [InlineData("execute_command")]
    [InlineData("modify_viewmodel")]
    [InlineData("override_style_setter")]
    [InlineData("invalidate_layout")]
    [InlineData("drag_and_drop")]
    [InlineData("simulate_keyboard")]
    [InlineData("force_binding_update")]
    [InlineData("scroll_to_element")]
    [InlineData("highlight_element")]
    public void DestructiveTools_ShouldBeMarkedDestructive(string toolName)
    {
        var tool = AllTools.First(t => t.Attr.Name == toolName);
        tool.Attr.Destructive.Should().BeTrue(
            $"'{toolName}' modifies the running application");
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("ping")]
    public void IdempotentTools_ShouldBeMarkedIdempotent(string toolName)
    {
        var tool = AllTools.First(t => t.Attr.Name == toolName);
        tool.Attr.Idempotent.Should().BeTrue(
            $"'{toolName}' is safe to call repeatedly");
    }

    [Fact]
    public void AllToolTypes_ShouldBeInMcpToolsNamespace()
    {
        var toolTypes = McpServerAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        foreach (var type in toolTypes)
        {
            type.Namespace.Should().Be("WpfDevTools.Mcp.Server.McpTools",
                $"tool type {type.Name} should be in McpTools namespace");
        }
    }

}
