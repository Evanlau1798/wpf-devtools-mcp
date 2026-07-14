using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Integration.E2E;

[Collection("McpE2E")]
[Trait("Category", "Integration")]
public sealed class McpToolSearchMetadataE2eTests
{
    [Fact]
    public async Task Initialize_ShouldDescribeNavigationEnvelopeForAdvancedClients()
    {
        using var client = new McpStdioClient();

        var init = await client.StartAsync(FindServerExecutable());

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
    public async Task ToolsList_ShouldExposeCanonicalSearchAndSchemaMetadata()
    {
        using var client = new McpStdioClient();
        var sourceTools = GetSourceRegisteredTools();
        await client.StartAsync(FindServerExecutable());

        await ExecuteChecksAsync(
            ("source parity", async () =>
            {
                var runtimeByName = (await ListRuntimeToolsAsync(client)).ToDictionary(
                    tool => tool.GetProperty("name").GetString()!,
                    StringComparer.Ordinal);
                runtimeByName.Keys.Should().OnlyHaveUniqueItems();
                runtimeByName.Keys.Should().BeEquivalentTo(sourceTools.Select(tool => tool.Name));
                runtimeByName.Count.Should().Be(sourceTools.Length);

                foreach (var sourceTool in sourceTools)
                {
                    var runtimeTool = runtimeByName[sourceTool.Name];
                    runtimeTool.GetProperty("title").GetString().Should().Be(sourceTool.Title);
                    runtimeTool.GetProperty("description").GetString().Should().Be(sourceTool.Description);
                    runtimeTool.GetProperty("inputSchema").ValueKind.Should().Be(JsonValueKind.Object);
                    runtimeTool.GetProperty("outputSchema").ValueKind.Should().Be(JsonValueKind.Object);
                }
            }),
            ("search titles", async () =>
            {
                var runtimeByName = (await ListRuntimeToolsAsync(client)).ToDictionary(
                    tool => tool.GetProperty("name").GetString()!,
                    StringComparer.Ordinal);
                AssertTitle(runtimeByName, "get_processes", "List Inspectable WPF Processes");
                AssertTitle(runtimeByName, "connect", "Connect To Running WPF Process");
                AssertTitle(runtimeByName, "get_visual_tree", "Inspect WPF Visual Tree");
                AssertTitle(runtimeByName, "get_binding_errors", "Diagnose WPF Binding Errors");
                AssertTitle(runtimeByName, "get_viewmodel", "Inspect WPF ViewModel");
                AssertTitle(
                    runtimeByName,
                    "wait_for_dp_change_after_mutation",
                    "Wait For WPF DependencyProperty Change After Mutation");
            }),
            ("output schemas", async () =>
            {
                var runtimeTools = await ListRuntimeToolsAsync(client);
                foreach (var tool in runtimeTools)
                {
                    var toolName = tool.GetProperty("name").GetString();
                    tool.TryGetProperty("outputSchema", out var outputSchema).Should().BeTrue(
                        $"tool '{toolName}' should advertise structured payload metadata");
                    outputSchema.ValueKind.Should().Be(JsonValueKind.Object,
                        $"tool '{toolName}' should expose outputSchema as an object schema");
                    outputSchema.TryGetProperty("properties", out var properties).Should().BeTrue(
                        $"tool '{toolName}' outputSchema should describe structured payload properties");
                    properties.TryGetProperty("success", out _).Should().BeTrue();
                    properties.TryGetProperty("navigation", out _).Should().BeTrue();
                    properties.TryGetProperty("structuredContent", out _).Should().BeFalse(
                        $"tool '{toolName}' outputSchema must not describe the CallToolResult envelope");
                }

                runtimeTools.Single(tool => tool.GetProperty("name").GetString() == "connect")
                    .GetProperty("outputSchema")
                    .GetProperty("properties")
                    .TryGetProperty("processId", out _)
                    .Should().BeTrue("connect should publish its primary process identifier field");
            }),
            ("wait annotations", async () =>
            {
                var runtimeByName = (await ListRuntimeToolsAsync(client)).ToDictionary(
                    tool => tool.GetProperty("name").GetString()!,
                    StringComparer.Ordinal);
                var readOnlyTool = runtimeByName["wait_for_dp_change"];
                var mutationTool = runtimeByName["wait_for_dp_change_after_mutation"];
                readOnlyTool.TryGetProperty("annotations", out var readOnlyAnnotations).Should().BeTrue();
                readOnlyAnnotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
                readOnlyAnnotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
                mutationTool.TryGetProperty("annotations", out var mutationAnnotations).Should().BeTrue();
                mutationAnnotations.GetProperty("destructiveHint").GetBoolean().Should().BeTrue();
                mutationAnnotations.TryGetProperty("readOnlyHint", out _).Should().BeFalse();
            }),
            ("wait input schemas", async () =>
            {
                var runtimeByName = (await ListRuntimeToolsAsync(client)).ToDictionary(
                    tool => tool.GetProperty("name").GetString()!,
                    StringComparer.Ordinal);
                var readOnlyProperties = runtimeByName["wait_for_dp_change"]
                    .GetProperty("inputSchema").GetProperty("properties");
                readOnlyProperties.TryGetProperty("triggerMutation", out _).Should().BeFalse();
                var mutationSchema = runtimeByName["wait_for_dp_change_after_mutation"].GetProperty("inputSchema");
                mutationSchema.GetProperty("properties").TryGetProperty("triggerMutation", out _).Should().BeTrue();
                mutationSchema.GetProperty("required").EnumerateArray()
                    .Select(item => item.GetString()).Should().Contain("triggerMutation");
            }),
            ("event drain input schema", async () =>
            {
                var drainEvents = (await ListRuntimeToolsAsync(client))
                    .Single(tool => tool.GetProperty("name").GetString() == "drain_events");
                var eventTypes = drainEvents.GetProperty("inputSchema")
                    .GetProperty("properties").GetProperty("eventTypes");

                eventTypes.GetProperty("type").EnumerateArray()
                    .Select(type => type.GetString()).Should().Contain("array");
                eventTypes.TryGetProperty("enum", out _).Should().BeFalse();
                eventTypes.GetProperty("items").GetProperty("enum").EnumerateArray()
                    .Select(value => value.GetString())
                    .Should().BeEquivalentTo("all", "DpChange", "RoutedEvent", "BindingError", "ValidationChange");
            }));
    }

    [Fact]
    public async Task Resources_ShouldExposeAndReadCanonicalContracts()
    {
        using var client = new McpStdioClient();
        await client.StartAsync(FindServerExecutable());

        await ExecuteChecksAsync(
            ("response resource listing", () => AssertResourceListedAsync(
                client, "wpf://contracts/response", "wpf_response_contract", "Response Contract")),
            ("contract index listing", () => AssertResourceListedAsync(
                client, "wpf://contracts/index", "wpf_contract_index", "Contract Resource Index")),
            ("contract chunk template listing", () => AssertResourceTemplateListedAsync(
                client,
                "wpf://contracts/{contractId}/chunks/{offset}/{length}",
                "wpf_contract_chunk",
                "Contract Resource Chunk")),
            ("tool manifest listing", () => AssertResourceListedAsync(
                client, "wpf://contracts/tools", "wpf_tool_manifest", "Tool Manifest")),
            ("tool examples listing", () => AssertResourceListedAsync(
                client, "wpf://contracts/tool-examples", "wpf_tool_examples", "Tool Input Examples")),
            ("response contract content", async () =>
            {
                using var contract = await ReadJsonResourceAsync(client, "wpf://contracts/response");
                var root = contract.RootElement;
                root.GetProperty("responseContractVersion").GetString().Should().Be(ResponseContractVersion.Current);
                root.GetProperty("toolPayload").GetProperty("canonicalField").GetString().Should().Be("structuredContent");
                root.GetProperty("navigation").GetProperty("field").GetString().Should().Be("navigation");
                root.GetProperty("nextSteps").GetProperty("derivedFrom").GetString().Should().Be("navigation.recommended");
                root.GetProperty("compatibility").GetProperty("toolListOutputSchema").GetString().Should().Be("advertised");
            }),
            ("tool manifest content", async () =>
            {
                using var manifest = await ReadJsonResourceAsync(client, "wpf://contracts/tools");
                var root = manifest.RootElement;
                var sourceTools = GetSourceRegisteredTools();
                var manifestNames = root.GetProperty("tools").EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString()).ToArray();
                root.GetProperty("toolCount").GetInt32().Should().Be(sourceTools.Length);
                manifestNames.Should().BeEquivalentTo(sourceTools.Select(tool => tool.Name));
            }),
            ("tool examples content", async () =>
            {
                using var document = await ReadJsonResourceAsync(client, "wpf://contracts/tool-examples");
                var examples = document.RootElement.GetProperty("examplesByTool");
                examples.GetProperty("batch_mutate").GetArrayLength().Should().BeGreaterThan(0);
                examples.GetProperty("wait_for_dp_change_after_mutation").GetArrayLength().Should().BeGreaterThan(0);
                examples.GetProperty("element_screenshot").EnumerateArray()
                    .Any(example => example.TryGetProperty("resourceFollowUp", out _)).Should().BeTrue();
            }),
            ("chunked response contract reconstruction", async () =>
            {
                using var index = await ReadJsonResourceAsync(client, "wpf://contracts/index");
                var entry = index.RootElement.GetProperty("resources").EnumerateArray()
                    .Single(resource => resource.GetProperty("id").GetString() == "response");
                var byteLength = entry.GetProperty("byteLength").GetInt32();
                var expectedSha256 = entry.GetProperty("sha256").GetString();
                var maxChunkBytes = index.RootElement.GetProperty("maxChunkBytes").GetInt32();
                using var reconstructed = new MemoryStream();

                for (var offset = 0; offset < byteLength; offset += maxChunkBytes)
                {
                    var bytes = await ReadBlobResourceAsync(
                        client,
                        $"wpf://contracts/response/chunks/{offset}/{maxChunkBytes}");
                    reconstructed.Write(bytes);
                }

                var content = reconstructed.ToArray();
                content.Length.Should().Be(byteLength);
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant().Should().Be(expectedSha256);
                using var reconstructedDocument = JsonDocument.Parse(content);
                reconstructedDocument.RootElement.GetProperty("resourceUri").GetString()
                    .Should().Be("wpf://contracts/response");
            }),
            ("missing screenshot resource", async () =>
            {
                var missing = await client.ReadResourceAsync("wpf://screenshots/shot_0123456789abcdef0123456789abcdef");
                missing.TryGetProperty("result", out _).Should().BeFalse();
                missing.TryGetProperty("error", out var error).Should().BeTrue();
                error.GetProperty("code").GetInt32().Should().Be((int)McpErrorCode.ResourceNotFound);
                error.GetProperty("message").GetString().Should().Contain("not retained");
            }));
    }

    [Fact]
    public async Task RawToolCallEnvelope_ShouldMatchPublishedResponseContractForErrorAnnotations()
    {
        using var client = new McpStdioClient();
        await client.StartAsync(FindServerExecutable());

        var response = await client.CallToolEnvelopeAsync("ping");
        var result = response.GetProperty("result");

        result.TryGetProperty("structuredContent", out var structuredContent).Should().BeTrue();
        structuredContent.GetProperty("success").GetBoolean().Should().BeFalse();

        var textBlock = result.GetProperty("content").EnumerateArray().Single();
        textBlock.TryGetProperty("text", out var text).Should().BeTrue();
        JsonDocument.Parse(text.GetString()!).RootElement.GetProperty("hasStructuredContent").GetBoolean().Should().BeTrue();
        textBlock.TryGetProperty("annotations", out var annotations).Should().BeTrue();
        annotations.GetProperty("priority").GetDouble().Should().Be(1.0d);
    }

    private static async Task ExecuteChecksAsync(params (string Name, Func<Task> Execute)[] checks)
    {
        using var aggregateScope = new AssertionScope();
        foreach (var check in checks)
        {
            using var checkScope = new AssertionScope(check.Name);
            try
            {
                await check.Execute();
            }
            catch (Exception exception)
            {
                false.Should().BeTrue(
                    "check {0} should not throw {1}: {2}",
                    check.Name,
                    exception.GetType().Name,
                    exception.Message);
            }
        }
    }

    private static async Task<JsonElement[]> ListRuntimeToolsAsync(McpStdioClient client)
    {
        var response = await client.ListToolsAsync();
        return response.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
    }

    private static async Task AssertResourceListedAsync(
        McpStdioClient client,
        string uri,
        string name,
        string title)
    {
        var response = await client.ListResourcesAsync();
        AssertResource(response.GetProperty("result").GetProperty("resources"), uri, name, title);
    }

    private static async Task AssertResourceTemplateListedAsync(
        McpStdioClient client,
        string uriTemplate,
        string name,
        string title)
    {
        var response = await client.ListResourceTemplatesAsync();
        var template = response.GetProperty("result").GetProperty("resourceTemplates")
            .EnumerateArray().Single(item => item.GetProperty("uriTemplate").GetString() == uriTemplate);
        template.GetProperty("name").GetString().Should().Be(name);
        template.GetProperty("title").GetString().Should().Be(title);
        template.GetProperty("mimeType").GetString().Should().Be("application/octet-stream");
    }

    private static void AssertTitle(
        IReadOnlyDictionary<string, JsonElement> tools,
        string toolName,
        string expectedTitle)
        => tools[toolName].GetProperty("title").GetString().Should().Be(expectedTitle);

    private static void AssertResource(JsonElement resources, string uri, string name, string title)
    {
        var resource = resources.EnumerateArray().Single(item => item.GetProperty("uri").GetString() == uri);
        resource.GetProperty("name").GetString().Should().Be(name);
        resource.GetProperty("title").GetString().Should().Be(title);
        resource.GetProperty("mimeType").GetString().Should().Be("application/json");
    }

    private static async Task<JsonDocument> ReadJsonResourceAsync(McpStdioClient client, string uri)
    {
        var response = await client.ReadResourceAsync(uri);
        var content = response.GetProperty("result").GetProperty("contents").EnumerateArray().Single();
        content.GetProperty("uri").GetString().Should().Be(uri);
        content.GetProperty("mimeType").GetString().Should().Be("application/json");
        return JsonDocument.Parse(content.GetProperty("text").GetString()!);
    }

    private static async Task<byte[]> ReadBlobResourceAsync(McpStdioClient client, string uri)
    {
        var response = await client.ReadResourceAsync(uri);
        var content = response.GetProperty("result").GetProperty("contents").EnumerateArray().Single();
        content.GetProperty("uri").GetString().Should().Be(uri);
        content.GetProperty("mimeType").GetString().Should().Be("application/octet-stream");
        return Convert.FromBase64String(content.GetProperty("blob").GetString()!);
    }

    private static string FindServerExecutable()
        => IntegrationExecutableLocator.FindExecutable(
               AppContext.BaseDirectory,
               "src",
               "WpfDevTools.Mcp.Server",
               "net8.0",
               "WpfDevTools.Mcp.Server.exe")
           ?? throw new InvalidOperationException(
               "WpfDevTools.Mcp.Server.exe was not found for the current test configuration. Build the MCP server first.");

    private static SourceTool[] GetSourceRegisteredTools()
        => typeof(ProcessMcpTools).Assembly.GetTypes()
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

    private sealed record SourceTool(string Name, string Title, string Description);
}
