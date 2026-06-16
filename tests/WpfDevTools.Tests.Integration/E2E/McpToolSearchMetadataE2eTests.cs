using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Schema;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "Integration")]
public sealed class McpToolSearchMetadataE2eTests
{
    [Fact]
    public async Task ToolsList_ShouldMatchSourceRegisteredToolSurface()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();
        var sourceTools = GetSourceRegisteredTools();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var runtimeTools = response.GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .ToArray();
        var runtimeByName = runtimeTools.ToDictionary(
            tool => tool.GetProperty("name").GetString()!,
            StringComparer.Ordinal);

        runtimeByName.Keys.Should().OnlyHaveUniqueItems();
        runtimeByName.Keys.Should().BeEquivalentTo(sourceTools.Select(tool => tool.Name));
        runtimeByName.Count.Should().Be(64);

        foreach (var sourceTool in sourceTools)
        {
            var runtimeTool = runtimeByName[sourceTool.Name];

            runtimeTool.GetProperty("title").GetString().Should().Be(sourceTool.Title);
            runtimeTool.GetProperty("description").GetString().Should().Be(sourceTool.Description);
            runtimeTool.GetProperty("inputSchema").ValueKind.Should().Be(JsonValueKind.Object);
            runtimeTool.GetProperty("outputSchema").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Fact]
    public async Task ToolsList_ShouldExposeSearchOptimizedAnchorTitles()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var tools = response.GetProperty("result").GetProperty("tools");

        AssertTitle(tools, "get_processes", "List Inspectable WPF Processes");
        AssertTitle(tools, "connect", "Connect To Running WPF Process");
        AssertTitle(tools, "get_visual_tree", "Inspect WPF Visual Tree");
        AssertTitle(tools, "get_binding_errors", "Diagnose WPF Binding Errors");
        AssertTitle(tools, "get_viewmodel", "Inspect WPF ViewModel");
        AssertTitle(tools, "wait_for_dp_change_after_mutation", "Wait For WPF DependencyProperty Change After Mutation");
    }

    [Fact]
    public async Task ToolsList_ShouldExposeStructuredPayloadOutputSchema()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var tools = response.GetProperty("result").GetProperty("tools");

        foreach (var tool in tools.EnumerateArray())
        {
            var toolName = tool.GetProperty("name").GetString();
            tool.TryGetProperty("outputSchema", out var outputSchema).Should().BeTrue(
                $"tool '{toolName}' should advertise structured payload metadata");
            outputSchema.ValueKind.Should().Be(JsonValueKind.Object,
                $"tool '{toolName}' should expose outputSchema as an object schema");
            outputSchema.TryGetProperty("properties", out var properties).Should().BeTrue(
                $"tool '{toolName}' outputSchema should describe result.structuredContent payload properties");
            properties.TryGetProperty("success", out _).Should().BeTrue(
                $"tool '{toolName}' should expose the common structuredContent success field");
            properties.TryGetProperty("navigation", out _).Should().BeTrue(
                $"tool '{toolName}' should expose the common navigation payload field");
            properties.TryGetProperty("structuredContent", out _).Should().BeFalse(
                $"tool '{toolName}' outputSchema must describe result.structuredContent itself, not the CallToolResult envelope");
        }

        var connect = tools.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == "connect");
        connect.GetProperty("outputSchema")
            .GetProperty("properties")
            .TryGetProperty("processId", out _)
            .Should().BeTrue("connect should publish its primary process identifier field in tools/list outputSchema");
    }

    [Fact]
    public async Task ToolsList_ShouldClassifyWaitForDpChangeAndMutationVariantSeparately()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var tools = response.GetProperty("result").GetProperty("tools");
        var readOnlyTool = tools.EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "wait_for_dp_change");
        var mutationTool = tools.EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "wait_for_dp_change_after_mutation");

        readOnlyTool.TryGetProperty("annotations", out var readOnlyAnnotations).Should().BeTrue(
            "tools/list should expose MCP hint annotations for AI-friendly tool selection");
        readOnlyAnnotations.TryGetProperty("readOnlyHint", out var readOnlyHint).Should().BeTrue(
            "wait_for_dp_change is now the public read-only wait contract");
        readOnlyHint.GetBoolean().Should().BeTrue();
        readOnlyAnnotations.TryGetProperty("destructiveHint", out var readOnlyDestructiveHint).Should().BeTrue(
            "the SDK currently emits an explicit non-destructive hint alongside read-only MCP metadata");
        readOnlyDestructiveHint.GetBoolean().Should().BeFalse();

        mutationTool.TryGetProperty("annotations", out var mutationAnnotations).Should().BeTrue(
            "the mutation variant should also expose MCP hint annotations");
        mutationAnnotations.TryGetProperty("destructiveHint", out var destructiveHint).Should().BeTrue(
            "wait_for_dp_change_after_mutation executes one live mutation before waiting");
        destructiveHint.GetBoolean().Should().BeTrue();
        mutationAnnotations.TryGetProperty("readOnlyHint", out _).Should().BeFalse(
            "the mutation variant should not advertise the opposite MCP hint");
    }

    [Fact]
    public async Task ToolsList_ShouldExposeSeparatedWaitInputSchemas()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListToolsAsync();
        var tools = response.GetProperty("result").GetProperty("tools");
        var readOnlyTool = tools.EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "wait_for_dp_change");
        var mutationTool = tools.EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "wait_for_dp_change_after_mutation");

        var readOnlyProperties = readOnlyTool.GetProperty("inputSchema").GetProperty("properties");
        readOnlyProperties.TryGetProperty("triggerMutation", out _).Should().BeFalse(
            "the public read-only wait tool should not advertise the mutation step in its schema");

        var mutationSchema = mutationTool.GetProperty("inputSchema");
        var mutationProperties = mutationSchema.GetProperty("properties");
        mutationProperties.TryGetProperty("triggerMutation", out _).Should().BeTrue(
            "the serialized mutation-plus-wait tool should require the mutation step in its schema");
        mutationSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Contain("triggerMutation");
    }

    [Fact]
    public async Task Initialize_ShouldDescribeNavigationEnvelopeForAdvancedClients()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        var init = await client.StartAsync(serverExe);
        init.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("instructions", out var instructions).Should().BeTrue();
        var text = instructions.GetString();

        text.Should().Contain("navigation");
        text.Should().Contain("nextSteps");
        text.Should().Contain("contextRefs");
        text.Should().Contain("prefetchTools");
        text.Should().Contain("wpf://contracts/response");
    }

    [Fact]
    public async Task ResourcesList_ShouldExposeMachineReadableResponseContractResource()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListResourcesAsync();
        var resources = response.GetProperty("result").GetProperty("resources");
        var resource = resources.EnumerateArray()
            .Single(item => item.GetProperty("uri").GetString() == "wpf://contracts/response");

        resource.GetProperty("name").GetString().Should().Be("wpf_response_contract");
        resource.GetProperty("title").GetString().Should().Be("Response Contract");
        resource.GetProperty("mimeType").GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task ResourcesList_ShouldExposeCanonicalToolManifestResource()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListResourcesAsync();
        var resources = response.GetProperty("result").GetProperty("resources");
        var resource = resources.EnumerateArray()
            .Single(item => item.GetProperty("uri").GetString() == "wpf://contracts/tools");

        resource.GetProperty("name").GetString().Should().Be("wpf_tool_manifest");
        resource.GetProperty("title").GetString().Should().Be("Tool Manifest");
        resource.GetProperty("mimeType").GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task ResourcesList_ShouldExposeToolExamplesResource()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ListResourcesAsync();
        var resources = response.GetProperty("result").GetProperty("resources");
        var resource = resources.EnumerateArray()
            .Single(item => item.GetProperty("uri").GetString() == "wpf://contracts/tool-examples");

        resource.GetProperty("name").GetString().Should().Be("wpf_tool_examples");
        resource.GetProperty("title").GetString().Should().Be("Tool Input Examples");
        resource.GetProperty("mimeType").GetString().Should().Be("application/json");
    }

    [Fact]
    public async Task ReadResource_ShouldReturnMachineReadableResponseContractJson()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ReadResourceAsync("wpf://contracts/response");
        var contents = response.GetProperty("result").GetProperty("contents");
        var content = contents.EnumerateArray().Single();

        content.GetProperty("uri").GetString().Should().Be("wpf://contracts/response");
        content.GetProperty("mimeType").GetString().Should().Be("application/json");

        using var document = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        var root = document.RootElement;

        root.GetProperty("responseContractVersion").GetString().Should().Be(ResponseContractVersion.Current);
        root.GetProperty("toolPayload").GetProperty("canonicalField").GetString().Should().Be("structuredContent");
        root.GetProperty("navigation").GetProperty("field").GetString().Should().Be("navigation");
        root.GetProperty("nextSteps").GetProperty("derivedFrom").GetString().Should().Be("navigation.recommended");
        root.GetProperty("compatibility").GetProperty("toolListOutputSchema").GetString().Should().Be("advertised");
    }

    [Fact]
    public async Task ReadResource_ShouldReturnCanonicalToolManifestJson()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();
        var sourceTools = GetSourceRegisteredTools();

        await client.StartAsync(serverExe);

        var response = await client.ReadResourceAsync("wpf://contracts/tools");
        var contents = response.GetProperty("result").GetProperty("contents");
        var content = contents.EnumerateArray().Single();

        content.GetProperty("uri").GetString().Should().Be("wpf://contracts/tools");
        content.GetProperty("mimeType").GetString().Should().Be("application/json");

        using var document = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        var root = document.RootElement;
        var manifestNames = root.GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        root.GetProperty("toolCount").GetInt32().Should().Be(64);
        manifestNames.Should().BeEquivalentTo(sourceTools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task ReadResource_ShouldReturnToolInputExamplesJson()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ReadResourceAsync("wpf://contracts/tool-examples");
        var contents = response.GetProperty("result").GetProperty("contents");
        var content = contents.EnumerateArray().Single();

        content.GetProperty("uri").GetString().Should().Be("wpf://contracts/tool-examples");
        content.GetProperty("mimeType").GetString().Should().Be("application/json");

        using var document = JsonDocument.Parse(content.GetProperty("text").GetString()!);
        var examples = document.RootElement.GetProperty("examplesByTool");

        examples.GetProperty("batch_mutate").GetArrayLength().Should().BeGreaterThan(0);
        examples.GetProperty("wait_for_dp_change_after_mutation").GetArrayLength().Should().BeGreaterThan(0);
        examples.GetProperty("element_screenshot").EnumerateArray()
            .Any(example => example.TryGetProperty("resourceFollowUp", out _))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ReadResource_WithMissingScreenshotResource_ShouldReturnResourceNotFoundError()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.ReadResourceAsync("wpf://screenshots/shot_0123456789abcdef0123456789abcdef");

        response.TryGetProperty("result", out _).Should().BeFalse();
        response.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be((int)McpErrorCode.ResourceNotFound);
        error.GetProperty("message").GetString().Should().Contain("not retained");
    }

    [Fact]
    public async Task RawToolCallEnvelope_ShouldMatchPublishedResponseContractForErrorAnnotations()
    {
        var serverExe = FindServerExecutable();
        using var client = new McpStdioClient();

        await client.StartAsync(serverExe);

        var response = await client.CallToolEnvelopeAsync("ping");
        var result = response.GetProperty("result");

        result.TryGetProperty("structuredContent", out var structuredContent).Should().BeTrue();
        structuredContent.GetProperty("success").GetBoolean().Should().BeFalse();

        var content = result.GetProperty("content");
        var textBlock = content.EnumerateArray().Single();
        textBlock.TryGetProperty("text", out var text).Should().BeTrue();
        JsonDocument.Parse(text.GetString()!).RootElement.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textBlock.TryGetProperty("annotations", out var annotations).Should().BeTrue();
        annotations.GetProperty("priority").GetDouble().Should().Be(1.0d);
    }

    private static void AssertTitle(JsonElement tools, string toolName, string expectedTitle)
    {
        var tool = tools.EnumerateArray()
            .Single(t => t.GetProperty("name").GetString() == toolName);

        tool.GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    private static string FindServerExecutable()
    {
        return IntegrationExecutableLocator.FindExecutable(
                AppContext.BaseDirectory,
                "src",
                "WpfDevTools.Mcp.Server",
                "net8.0",
                "WpfDevTools.Mcp.Server.exe")
            ?? throw new InvalidOperationException(
                "WpfDevTools.Mcp.Server.exe was not found for the current test configuration. Build the MCP server first.");
    }

    private static SourceTool[] GetSourceRegisteredTools()
    {
        return typeof(ProcessMcpTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => new
            {
                Attribute = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
            })
            .Where(item => item.Attribute?.Name is not null)
            .Select(item => new SourceTool(
                item.Attribute!.Name!,
                item.Attribute.Title ?? string.Empty,
                item.Description ?? string.Empty))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record SourceTool(string Name, string Title, string Description);
}
