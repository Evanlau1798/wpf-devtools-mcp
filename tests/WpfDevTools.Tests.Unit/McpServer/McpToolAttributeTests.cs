using System.ComponentModel;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolAttributeTests
{
    private static readonly Assembly McpServerAssembly = typeof(ServerInstructions).Assembly;
    private static readonly IReadOnlyList<ToolMetadata> AllTools = GetAllToolMethods();
    private static readonly HashSet<string> AllToolNames =
        AllTools.Select(tool => tool.Attribute.Name)
            .Where(name => name != null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static readonly string[] ExpectedPublishedToolNames =
    [
        "get_processes", "connect", "ping",
        "get_visual_tree", "get_logical_tree", "serialize_to_xaml", "get_namescope", "get_template_tree", "compare_trees",
        "get_bindings", "get_binding_errors", "get_binding_value_chain", "get_datacontext_chain", "force_binding_update",
        "get_dp_value_source", "get_dp_metadata", "set_dp_value", "clear_dp_value", "watch_dp_changes", "wait_for_dp_change", "wait_for_dp_change_after_mutation",
        "get_applied_styles", "get_triggers", "get_resource_chain", "override_style_setter",
        "trace_routed_events", "get_event_handlers", "fire_routed_event", "drain_events",
        "click_element", "drag_and_drop", "get_focus_state", "focus_element", "scroll_to_element", "simulate_keyboard", "element_screenshot",
        "capture_state_snapshot", "restore_state_snapshot", "batch_mutate",
        "get_layout_info", "get_clipping_info", "highlight_element", "invalidate_layout",
        "get_viewmodel", "get_commands", "execute_command", "modify_viewmodel", "get_validation_errors",
        "get_render_stats", "find_binding_leaks", "measure_element_render_time", "get_visual_count"
    ];

    private static readonly string[] ReadOnlyToolNames =
    [
        "get_processes", "get_visual_tree", "get_logical_tree", "get_bindings", "get_binding_errors",
        "get_dp_value_source", "get_applied_styles", "get_event_handlers", "element_screenshot",
        "get_focus_state", "get_layout_info", "get_viewmodel", "get_render_stats", "wait_for_dp_change"
    ];

    private static readonly string[] DestructiveToolNames =
    [
        "connect", "set_dp_value", "clear_dp_value", "click_element", "fire_routed_event",
        "execute_command", "modify_viewmodel", "override_style_setter", "invalidate_layout",
        "drag_and_drop", "focus_element", "simulate_keyboard", "force_binding_update",
        "wait_for_dp_change_after_mutation", "scroll_to_element", "highlight_element",
        "restore_state_snapshot", "batch_mutate"
    ];

    private static readonly HashSet<string> ToolsWithComplexInlineExamples = new(StringComparer.Ordinal)
    {
        "batch_mutate",
        "element_screenshot",
        "wait_for_dp_change_after_mutation"
    };

    [Fact]
    public void AllTools_ShouldExposeStableDiscoveryMetadata()
    {
        using var scope = new AssertionScope();
        AllTools.Select(tool => tool.Attribute.Name).Should().OnlyHaveUniqueItems();

        foreach (var tool in AllTools)
        {
            var description = tool.Method.GetCustomAttribute<DescriptionAttribute>();
            tool.Attribute.Name.Should().NotBeNullOrWhiteSpace($"tool method {tool.Method.Name} must have a protocol name");
            tool.Attribute.Title.Should().NotBeNullOrWhiteSpace($"tool '{tool.Attribute.Name}' must have a friendly title");
            tool.Attribute.OpenWorld.Should().BeFalse($"tool '{tool.Attribute.Name}' uses local WPF and IPC state");
            tool.Attribute.UseStructuredContent.Should().BeTrue($"tool '{tool.Attribute.Name}' returns structured content");
            description.Should().NotBeNull($"tool '{tool.Attribute.Name}' in {tool.Type.Name} must have [Description]");
            description?.Description.Should().NotBeNullOrWhiteSpace($"tool '{tool.Attribute.Name}' description must not be empty");
            description?.Description.Should().Contain("CATEGORY:", $"tool '{tool.Attribute.Name}' must expose its category");
            if (tool.Type == typeof(UiComposerMcpTools)
                || ToolsWithComplexInlineExamples.Contains(tool.Attribute.Name!))
            {
                description?.Description.Should().Contain(
                    "EXAMPLES:",
                    $"tool '{tool.Attribute.Name}' has a non-obvious input or recovery shape that warrants an inline example");
            }

            description?.Description.Should().NotContain("Examples:", $"tool '{tool.Attribute.Name}' must use a stable heading");

            foreach (var parameter in tool.Method.GetParameters().Where(IsMcpExposedParameter))
            {
                var parameterDescription = parameter.GetCustomAttribute<DescriptionAttribute>();
                parameterDescription.Should().NotBeNull(
                    $"tool '{tool.Attribute.Name}' parameter '{parameter.Name}' must have [Description]");
                parameterDescription?.Description.Should().NotBeNullOrWhiteSpace(
                    $"tool '{tool.Attribute.Name}' parameter '{parameter.Name}' description must not be empty");
            }
        }

        ServerInstructions.Value.Should().Contain(
            "wpf://contracts/tool-examples",
            "trivial examples should be progressively discoverable instead of repeated in every tool description");
    }

    [Fact]
    public void EstablishedToolNames_ShouldRemainPublished()
    {
        ExpectedPublishedToolNames.Should().OnlyHaveUniqueItems();
        ExpectedPublishedToolNames.Should().BeSubsetOf(AllToolNames);
    }

    [Fact]
    public void ToolSafetyAnnotations_ShouldMatchContract()
    {
        using var scope = new AssertionScope();
        foreach (var name in ReadOnlyToolNames)
        {
            GetRequiredTool(name).Attribute.ReadOnly.Should().BeTrue($"'{name}' is a read-only inspection tool");
        }

        foreach (var name in DestructiveToolNames)
        {
            GetRequiredTool(name).Attribute.Destructive.Should().BeTrue($"'{name}' modifies the running application");
        }

        foreach (var name in new[] { "connect", "ping" })
        {
            GetRequiredTool(name).Attribute.Idempotent.Should().BeTrue($"'{name}' is safe to call repeatedly");
        }
    }

    [Fact]
    public void RepresentativeTools_ShouldPublishStructuredPayloadSchemas()
    {
        var representatives = new[]
        {
            (typeof(ProcessMcpTools), nameof(ProcessMcpTools.Ping)),
            (typeof(TreeMcpTools), nameof(TreeMcpTools.GetVisualTree)),
            (typeof(BindingMcpTools), nameof(BindingMcpTools.GetBindingErrors))
        };

        using var scope = new AssertionScope();
        foreach (var (toolType, methodName) in representatives)
        {
            var protocolTool = CreateTool(toolType, methodName).ProtocolTool;
            McpToolOutputSchemas.Apply(protocolTool);

            protocolTool.OutputSchema.Should().NotBeNull($"{methodName} must publish an output schema");
            var properties = protocolTool.OutputSchema!.Value.GetProperty("properties");
            properties.TryGetProperty("success", out _).Should().BeTrue();
            properties.TryGetProperty("navigation", out _).Should().BeTrue();
            properties.TryGetProperty("structuredContent", out _).Should().BeFalse();
        }
    }

    [Fact]
    public void ToolTypes_ShouldUseTheMcpToolsNamespace()
    {
        var toolTypes = McpServerAssembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null);

        toolTypes.Should().OnlyContain(
            type => type.Namespace == "WpfDevTools.Mcp.Server.McpTools");
    }

    [Fact]
    public void ProcessTools_ShouldAdvertiseStableTitles()
    {
        var expectedTitles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ProcessMcpTools.GetProcesses)] = "List Inspectable WPF Processes",
            [nameof(ProcessMcpTools.Connect)] = "Connect To Running WPF Process",
            [nameof(ProcessMcpTools.Ping)] = "Ping WPF Inspector Session"
        };

        using var scope = new AssertionScope();
        foreach (var (methodName, expectedTitle) in expectedTitles)
        {
            var method = typeof(ProcessMcpTools).GetMethod(methodName);
            method.Should().NotBeNull();
            var attribute = method?.GetCustomAttribute<McpServerToolAttribute>();
            attribute.Should().NotBeNull();
            attribute?.Title.Should().Be(expectedTitle);
            attribute?.UseStructuredContent.Should().BeTrue();
        }
    }

    private static IReadOnlyList<ToolMetadata> GetAllToolMethods()
        => McpServerAssembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => new { Type = type, Method = method, Attribute = method.GetCustomAttribute<McpServerToolAttribute>() }))
            .Where(tool => tool.Attribute != null)
            .Select(tool => new ToolMetadata(tool.Type, tool.Method, tool.Attribute!))
            .ToArray();

    private static bool IsMcpExposedParameter(ParameterInfo parameter)
        => parameter.ParameterType != typeof(SessionManager)
            && parameter.ParameterType != typeof(CancellationToken);

    private static ToolMetadata GetRequiredTool(string name)
        => AllTools.Single(tool => tool.Attribute.Name == name);

    private static McpServerTool CreateTool(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();
        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();
        return McpServerTool.Create(method!, target: null, new McpServerToolCreateOptions { Services = services });
    }

    private sealed record ToolMetadata(
        Type Type,
        MethodInfo Method,
        McpServerToolAttribute Attribute);
}
