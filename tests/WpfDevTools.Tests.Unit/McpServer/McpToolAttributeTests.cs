using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

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
    public void AllTools_ShouldIncludeStructuredCategoryMetadata_ForAiFriendlyDiscovery()
    {
        foreach (var (_, method, attr) in AllTools)
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>();

            description.Should().NotBeNull();
            description!.Description.Should().Contain("CATEGORY:",
                $"tool '{attr.Name}' should expose a category header for AI-friendly discovery");
        }
    }

    [Fact]
    public void AllTools_ShouldUseUppercaseExamplesHeading_ForConsistentPromptExtraction()
    {
        foreach (var (_, method, attr) in AllTools)
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>();

            description.Should().NotBeNull();
            description!.Description.Should().Contain("EXAMPLES:",
                $"tool '{attr.Name}' should use a consistent EXAMPLES heading for prompt extraction");
            description.Description.Should().NotContain("Examples:",
                $"tool '{attr.Name}' should avoid mixed examples heading casing");
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

    [Fact]
    public void AllTools_ShouldHaveFriendlyTitles_ForToolSearchDrivenClients()
    {
        foreach (var (_, _, attr) in AllTools)
        {
            attr.Title.Should().NotBeNullOrWhiteSpace(
                $"tool '{attr.Name}' should expose a human-friendly title for MCP tool search");
        }
    }

    [Fact]
    public void AllTools_ShouldAdvertiseStructuredContentMetadata_WhenResultsPopulateStructuredContent()
    {
        foreach (var (_, _, attr) in AllTools)
        {
            attr.UseStructuredContent.Should().BeTrue(
                $"tool '{attr.Name}' should truthfully advertise structured content and allow SDK-generated tools/list outputSchema metadata");
        }
    }

    [Theory]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools.Ping))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools.GetVisualTree))]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.BindingMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.BindingMcpTools.GetBindingErrors))]
    public void RepresentativeTools_ShouldExposeStructuredPayloadOutputSchema_WhenPublishedInToolsList(Type toolType, string methodName)
    {
        var protocolTool = CreateTool(toolType, methodName).ProtocolTool;
        McpToolOutputSchemas.Apply(protocolTool);
        var outputSchema = protocolTool.OutputSchema;

        outputSchema.Should().NotBeNull(
            "tools/list should publish a structuredContent payload schema for schema-driven clients");
        outputSchema!.Value.TryGetProperty("properties", out var properties).Should().BeTrue();
        properties.TryGetProperty("success", out _).Should().BeTrue(
            "the schema should describe result.structuredContent, including its common success field");
        properties.TryGetProperty("navigation", out _).Should().BeTrue(
            "the schema should describe the common navigation field when it is present");
        properties.TryGetProperty("structuredContent", out _).Should().BeFalse(
            "tools/list outputSchema should describe result.structuredContent itself, not the CallToolResult envelope");
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
    [InlineData("wait_for_dp_change")]
    [InlineData("wait_for_dp_change_after_mutation")]
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
    [InlineData("drain_events")]
    public void EventTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("click_element")]
    [InlineData("drag_and_drop")]
    [InlineData("get_focus_state")]
    [InlineData("focus_element")]
    [InlineData("scroll_to_element")]
    [InlineData("simulate_keyboard")]
    [InlineData("element_screenshot")]
    public void InteractionTools_ShouldExist(string toolName)
    {
        AllTools.Should().Contain(t => t.Attr.Name == toolName);
    }

    [Theory]
    [InlineData("capture_state_snapshot")]
    [InlineData("restore_state_snapshot")]
    [InlineData("batch_mutate")]
    public void StateTools_ShouldExist(string toolName)
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
    [InlineData("get_focus_state")]
    [InlineData("get_layout_info")]
    [InlineData("get_viewmodel")]
    [InlineData("get_render_stats")]
    [InlineData("wait_for_dp_change")]
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
    [InlineData("focus_element")]
    [InlineData("simulate_keyboard")]
    [InlineData("force_binding_update")]
    [InlineData("wait_for_dp_change_after_mutation")]
    [InlineData("scroll_to_element")]
    [InlineData("highlight_element")]
    [InlineData("restore_state_snapshot")]
    [InlineData("batch_mutate")]
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

    [Theory]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools.GetProcesses), "List Inspectable WPF Processes")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools.Connect), "Connect To Running WPF Process")]
    [InlineData(typeof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools.Ping), "Ping WPF Inspector Session")]
    public void ProcessTools_ShouldAdvertiseFriendlyTitlesAndStructuredContentMetadata(Type toolType, string methodName, string expectedTitle)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        var attr = method!.GetCustomAttribute<McpServerToolAttribute>();
        attr.Should().NotBeNull();
        attr!.Title.Should().Be(expectedTitle,
            "AI-facing clients should receive a stable human-friendly tool title");
        attr.UseStructuredContent.Should().BeTrue(
            "process tools return CallToolResult values and should participate in structuredContent outputSchema publication");
    }

    [Fact]
    public void HighValueToolInputSchemas_ShouldExposeParameterConstraints()
    {
        var getProcessesSchema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools.GetProcesses));
        AssertEnumConstraint(getProcessesSchema, "windowFilter", "visible", "all", "foreground");

        var connectSchema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.ProcessMcpTools.Connect));
        AssertIntegerConstraint(connectSchema, "processId", minimum: 1, maximum: int.MaxValue);
        AssertEnumConstraint(connectSchema, "selectionStrategy", "single_only", "largest_working_set");
        AssertEnumConstraint(connectSchema, "windowFilter", "visible", "all", "foreground");

        var visualTreeSchema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools.GetVisualTree));
        AssertIntegerConstraint(visualTreeSchema, "depth", minimum: 0, maximum: 100);
        AssertIntegerConstraint(visualTreeSchema, "maxNodes", minimum: 1, maximum: 10000);
        AssertIntegerConstraint(visualTreeSchema, "maxChildrenPerNode", minimum: 1, maximum: 1000);

        var logicalTreeSchema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools.GetLogicalTree));
        AssertIntegerConstraint(logicalTreeSchema, "depth", minimum: 0, maximum: 100);
        AssertIntegerConstraint(logicalTreeSchema, "maxNodes", minimum: 1, maximum: 10000);
        AssertIntegerConstraint(logicalTreeSchema, "maxChildrenPerNode", minimum: 1, maximum: 1000);

        var findElementsSchema = CreateInputSchema(typeof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools), nameof(WpfDevTools.Mcp.Server.McpTools.TreeMcpTools.FindElements));
        AssertIntegerConstraint(findElementsSchema, "maxTraversalNodes", minimum: 1, maximum: 10000);
        AssertStringMaxLength(findElementsSchema, "propertyName", 256);
        AssertEnumConstraint(findElementsSchema, "typeMatchMode", "exact", "assignable");
        var uiSummarySchema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.SceneDiagnosticsMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.SceneDiagnosticsMcpTools.GetUiSummary));
        AssertIntegerConstraint(uiSummarySchema, "depth", minimum: 0, maximum: 100);
        AssertEnumConstraint(uiSummarySchema, "depthMode", "semantic", "visual");

        var screenshotSchema = CreateInputSchema(
            typeof(WpfDevTools.Mcp.Server.McpTools.InteractionMcpTools),
            nameof(WpfDevTools.Mcp.Server.McpTools.InteractionMcpTools.ElementScreenshot));
        AssertEnumConstraint(screenshotSchema, "outputMode", "metadata", "file", "base64");
        AssertIntegerConstraint(screenshotSchema, "maxWidth", minimum: 1, maximum: int.MaxValue);
        AssertIntegerConstraint(screenshotSchema, "maxHeight", minimum: 1, maximum: int.MaxValue);
        foreach (var methodName in typeof(UiComposerMcpTools).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(method => method.GetParameters().Any(parameter => parameter.Name == "blueprintJson")).Select(method => method.Name))
            AssertStringMaxLength(CreateInputSchema(typeof(UiComposerMcpTools), methodName), "blueprintJson", 8192);
        var previewSchema = CreateInputSchema(typeof(UiComposerMcpTools), nameof(UiComposerMcpTools.PreviewUiBlueprint));
        AssertEnumConstraint(previewSchema, "screenshotOutputMode", "metadata", "file");
    }

    private static JsonElement CreateInputSchema(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        return CreateTool(toolType, methodName).ProtocolTool.InputSchema;
    }

    private static McpServerTool CreateTool(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName);
        method.Should().NotBeNull();

        using var services = new ServiceCollection()
            .AddSingleton<SessionManager>(_ => throw new InvalidOperationException("Schema tests do not invoke tools."))
            .BuildServiceProvider();
        return McpServerTool.Create(method!, target: null, new McpServerToolCreateOptions { Services = services });
    }

    private static void AssertIntegerConstraint(JsonElement schema, string parameterName, int? minimum, int? maximum)
    {
        var parameter = GetSchemaProperty(schema, parameterName);
        AssertSchemaTypeContains(parameter.GetProperty("type"), "integer");
        AssertNullableIntSchemaKeyword(parameter, "minimum", minimum);
        AssertNullableIntSchemaKeyword(parameter, "maximum", maximum);
    }

    private static void AssertSchemaTypeContains(JsonElement typeElement, string expectedType)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            typeElement.GetString().Should().Be(expectedType);
            return;
        }

        typeElement.ValueKind.Should().Be(JsonValueKind.Array);
        typeElement.EnumerateArray()
            .Select(type => type.GetString())
            .Should().Contain(expectedType);
    }

    private static void AssertEnumConstraint(JsonElement schema, string parameterName, params string[] expectedValues)
    {
        var values = GetSchemaProperty(schema, parameterName)
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        values.Should().BeEquivalentTo(expectedValues);
    }

    private static JsonElement GetSchemaProperty(JsonElement schema, string parameterName)
        => schema.GetProperty("properties").GetProperty(parameterName);

    private static void AssertStringMaxLength(JsonElement schema, string parameterName, int maxLength)
    {
        var parameter = GetSchemaProperty(schema, parameterName);
        AssertSchemaTypeContains(parameter.GetProperty("type"), "string");
        parameter.GetProperty("maxLength").GetInt32().Should().Be(maxLength);
    }

    private static void AssertNullableIntSchemaKeyword(JsonElement property, string keyword, int? expectedValue)
    {
        if (!expectedValue.HasValue)
        {
            property.TryGetProperty(keyword, out _).Should().BeFalse();
            return;
        }

        property.GetProperty(keyword).GetInt32().Should().Be(expectedValue.Value);
    }
}
