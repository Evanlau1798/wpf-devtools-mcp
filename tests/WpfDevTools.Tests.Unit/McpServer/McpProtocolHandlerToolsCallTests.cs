using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpProtocolHandlerToolsCallTests
{
    // ---------------------------------------------------------------------------
    // Helper factory methods
    // ---------------------------------------------------------------------------

    private static McpProtocolHandler CreateHandlerWithTool(
        string toolName,
        Func<JsonElement?, CancellationToken, Task<object>>? handler = null)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(new ToolDefinition
        {
            Name = toolName,
            Description = "Test tool",
            Parameters = new { },
            ExecuteHandler = handler
        });
        return new McpProtocolHandler(registry);
    }

    private static string BuildToolsCallRequest(int id, object? paramsObj)
    {
        if (paramsObj is null)
        {
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call" });
        }

        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = paramsObj });
    }

    // ---------------------------------------------------------------------------
    // 1. tools/call with missing params -> -32602
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithMissingParams_ShouldReturnInvalidParams()
    {
        var handler = CreateHandlerWithTool("test_tool");
        var request = BuildToolsCallRequest(id: 1, paramsObj: null);

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        responseObj.GetProperty("id").GetInt32().Should().Be(1);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
        responseObj.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("params");
    }

    // ---------------------------------------------------------------------------
    // 2. tools/call with params present but missing name property -> -32602
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithMissingName_ShouldReturnInvalidParams()
    {
        var handler = CreateHandlerWithTool("test_tool");
        var request = BuildToolsCallRequest(id: 2, paramsObj: new { arguments = new { } });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
        responseObj.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("name");
    }

    // ---------------------------------------------------------------------------
    // 3. tools/call with empty name -> -32602
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithEmptyName_ShouldReturnInvalidParams()
    {
        var handler = CreateHandlerWithTool("test_tool");
        var request = BuildToolsCallRequest(id: 3, paramsObj: new { name = "" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
        responseObj.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("name");
    }

    // ---------------------------------------------------------------------------
    // 4. tools/call with unknown tool -> -32601
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithUnknownTool_ShouldReturnMethodNotFound()
    {
        var handler = CreateHandlerWithTool("known_tool", (_, _) => Task.FromResult<object>(new { success = true }));
        var request = BuildToolsCallRequest(id: 4, paramsObj: new { name = "no_such_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
        responseObj.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("no_such_tool");
    }

    // ---------------------------------------------------------------------------
    // 5. tools/call with tool that has no ExecuteHandler -> -32603
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithNoExecuteHandler_ShouldReturnInternalError()
    {
        // Register tool without a handler (ExecuteHandler = null)
        var registry = new ToolRegistry();
        registry.RegisterTool(new ToolDefinition
        {
            Name = "handler_less_tool",
            Description = "Tool with no handler",
            Parameters = new { },
            ExecuteHandler = null
        });
        var handler = new McpProtocolHandler(registry);
        var request = BuildToolsCallRequest(id: 5, paramsObj: new { name = "handler_less_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32603);
    }

    // ---------------------------------------------------------------------------
    // 6. tools/call success -> content array with type=text
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithValidTool_ShouldReturnContentArray()
    {
        var handler = CreateHandlerWithTool(
            "my_tool",
            (_, _) => Task.FromResult<object>(new { success = true, data = "hello" }));

        var request = BuildToolsCallRequest(id: 6, paramsObj: new { name = "my_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);

        responseObj.TryGetProperty("error", out _).Should().BeFalse("a successful call must not contain an error field");
        var result = responseObj.GetProperty("result");
        var content = result.GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------------------
    // 7. tools/call where result has success=false -> isError=true
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithToolReturningError_ShouldSetIsError()
    {
        var handler = CreateHandlerWithTool(
            "failing_tool",
            (_, _) => Task.FromResult<object>(new { success = false, message = "something went wrong" }));

        var request = BuildToolsCallRequest(id: 7, paramsObj: new { name = "failing_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);

        // The JSON-RPC layer itself succeeds (no error property at the outer level)
        responseObj.TryGetProperty("error", out _).Should().BeFalse();

        var result = responseObj.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("content").GetArrayLength().Should().BeGreaterThan(0);
        result.GetProperty("content")[0].GetProperty("type").GetString().Should().Be("text");
    }

    // ---------------------------------------------------------------------------
    // 8. Notification handling (no id field) -> null response
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_WithNotification_ShouldReturnNull()
    {
        var handler = new McpProtocolHandler();
        // notifications/initialized has no "id" field
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().BeNull();
    }

    [Fact]
    public async Task HandleRequest_WithUnknownNotification_ShouldReturnNull()
    {
        var handler = new McpProtocolHandler();
        // Unknown notification with no "id" should also be silently ignored
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "some/unknownNotification"
        });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // 9. Request with missing method -> -32600
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_WithMissingMethod_ShouldReturnInvalidRequest()
    {
        var handler = new McpProtocolHandler();
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 9 });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32600);
    }

    [Fact]
    public async Task HandleRequest_WithEmptyMethod_ShouldReturnInvalidRequest()
    {
        var handler = new McpProtocolHandler();
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 10, method = "" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32600);
    }

    // ---------------------------------------------------------------------------
    // 10. SanitizeErrorMessage: file paths removed from error messages
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_WhenHandlerThrowsWithFilePath_ShouldSanitizePathInError()
    {
        // Arrange: handler throws with a Windows file path in the message
        var handler = CreateHandlerWithTool(
            "path_tool",
            (_, _) => throw new Exception(@"Failed reading C:\Users\secret\config.json"));

        var request = BuildToolsCallRequest(id: 11, paramsObj: new { name = "path_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32603);

        var message = responseObj.GetProperty("error").GetProperty("message").GetString()!;
        // The raw Windows path should have been replaced with [path]
        message.Should().NotContain(@"C:\Users\secret\config.json");
        message.Should().Contain("[path]");
    }

    [Fact]
    public async Task HandleRequest_WhenHandlerThrowsWithUnixPath_ShouldSanitizePathInError()
    {
        var handler = CreateHandlerWithTool(
            "unix_path_tool",
            (_, _) => throw new Exception("Failed reading /home/user/secret/config.json"));

        var request = BuildToolsCallRequest(id: 12, paramsObj: new { name = "unix_path_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        var message = responseObj.GetProperty("error").GetProperty("message").GetString()!;
        message.Should().NotContain("/home/user/secret/config.json");
        message.Should().Contain("[path]");
    }

    // ---------------------------------------------------------------------------
    // 11. String id -> properly round-tripped as string
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_WithStringId_ShouldPreserveStringIdInResponse()
    {
        var handler = new McpProtocolHandler();
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = "req-abc-123",
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                clientInfo = new { name = "test", version = "1.0" }
            }
        });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
        responseObj.GetProperty("id").GetString().Should().Be("req-abc-123");
    }

    [Fact]
    public async Task HandleRequest_WithStringId_OnError_ShouldPreserveStringIdInErrorResponse()
    {
        var handler = new McpProtocolHandler();
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = "err-id-xyz",
            method = "tools/call"
            // no params -> InvalidParams
        });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.GetProperty("id").GetString().Should().Be("err-id-xyz");
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    // ---------------------------------------------------------------------------
    // Additional: tools/call success passes arguments to handler
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_ShouldPassArgumentsToHandler()
    {
        JsonElement? capturedArgs = null;

        var handler = CreateHandlerWithTool(
            "arg_tool",
            (args, _) =>
            {
                capturedArgs = args;
                return Task.FromResult<object>(new { success = true });
            });

        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 20,
            method = "tools/call",
            @params = new
            {
                name = "arg_tool",
                arguments = new { elementId = 42, depth = 3 }
            }
        });

        await handler.HandleRequestAsync(request, CancellationToken.None);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Value.GetProperty("elementId").GetInt32().Should().Be(42);
        capturedArgs.Value.GetProperty("depth").GetInt32().Should().Be(3);
    }

    // ---------------------------------------------------------------------------
    // Additional: tools/call without arguments field is still valid
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task HandleRequest_ToolsCall_WithoutArgumentsField_ShouldSucceed()
    {
        JsonElement? capturedArgs = null;

        var handler = CreateHandlerWithTool(
            "no_arg_tool",
            (args, _) =>
            {
                capturedArgs = args;
                return Task.FromResult<object>(new { success = true });
            });

        var request = BuildToolsCallRequest(id: 21, paramsObj: new { name = "no_arg_tool" });

        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response!);
        responseObj.TryGetProperty("error", out _).Should().BeFalse();
        capturedArgs.Should().BeNull("no arguments property was supplied");
    }
}
