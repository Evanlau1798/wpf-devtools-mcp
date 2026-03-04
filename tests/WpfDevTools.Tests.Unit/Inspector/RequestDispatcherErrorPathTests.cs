using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Inspector;

/// <summary>
/// Tests for the error-handling branches in RequestDispatcher.DispatchAsync:
///   1. OperationCanceledException  → ErrorCode.InternalError ("cancelled or timed out")
///   2. ArgumentException           → ErrorCode.InvalidParams  (missing required param)
///   3. General Exception           → ErrorCode.InternalError  (with exception message)
///   4. Request Id is always preserved in error responses
/// </summary>
public class RequestDispatcherErrorPathTests
{
    // ── Helper ───────────────────────────────────────────────────────────────

    private static WpfDevTools.Inspector.Host.RequestDispatcher CreateDispatcher() =>
        new WpfDevTools.Inspector.Host.RequestDispatcher();

    // ── Cancellation error path ───────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_WithAlreadyCancelledToken_ShouldReturnCancellationError()
    {
        var dispatcher = CreateDispatcher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // get_visual_tree uses Task.Run internally → respects cancellation token
        var request = new InspectorRequest
        {
            Id = "cancel-01",
            Method = "get_visual_tree",
            Params = null
        };

        var response = await dispatcher.DispatchAsync(request, cts.Token);

        response.Should().NotBeNull();
        response.Id.Should().Be("cancel-01");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InternalError);
        response.Error.Message.Should().ContainEquivalentOf("cancelled");
    }

    [Fact]
    public async Task DispatchAsync_WithCancelledToken_ShouldPreserveRequestId()
    {
        var dispatcher = CreateDispatcher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        const string expectedId = "preserve-id-cancel";
        var request = new InspectorRequest
        {
            Id = expectedId,
            Method = "get_logical_tree",
            Params = null
        };

        var response = await dispatcher.DispatchAsync(request, cts.Token);

        response.Id.Should().Be(expectedId, "the response Id must always match the request Id");
    }

    // ── ArgumentException error paths (InvalidParams) ────────────────────────

    [Fact]
    public async Task DispatchAsync_ForceBindingUpdate_WithoutPropertyName_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-01",
            Method = "force_binding_update",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-1" })
            // propertyName intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Should().NotBeNull();
        response.Id.Should().Be("arg-01");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("propertyName");
    }

    [Fact]
    public async Task DispatchAsync_GetDpValueSource_WithoutPropertyName_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-02",
            Method = "get_dp_value_source",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-2" })
            // propertyName intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("propertyName");
    }

    [Fact]
    public async Task DispatchAsync_ExecuteCommand_WithoutCommandName_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-03",
            Method = "execute_command",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-3" })
            // commandName intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("commandName");
    }

    [Fact]
    public async Task DispatchAsync_GetBindingValueChain_WithoutPropertyName_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-04",
            Method = "get_binding_value_chain",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-4" })
            // propertyName intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("propertyName");
    }

    [Fact]
    public async Task DispatchAsync_SimulateKeyboard_WithoutKey_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-05",
            Method = "simulate_keyboard",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-5" })
            // key intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("key");
    }

    [Fact]
    public async Task DispatchAsync_SetDpValue_WithoutPropertyName_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-06",
            Method = "set_dp_value",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-6", value = "someValue" })
            // propertyName intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("propertyName");
    }

    [Fact]
    public async Task DispatchAsync_ClearDpValue_WithoutPropertyName_ShouldReturnInvalidParams()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "arg-07",
            Method = "clear_dp_value",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem-7" })
            // propertyName intentionally omitted
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        response.Error.Message.Should().Contain("propertyName");
    }

    // ── ArgumentException preserves request Id ───────────────────────────────

    [Fact]
    public async Task DispatchAsync_InvalidParams_ShouldPreserveRequestId()
    {
        var dispatcher = CreateDispatcher();
        const string expectedId = "preserve-id-arg";
        var request = new InspectorRequest
        {
            Id = expectedId,
            Method = "execute_command",
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem" })
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Id.Should().Be(expectedId, "response Id must mirror request Id even on error");
    }

    // ── Method not found error path ───────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_WithUnknownMethod_ShouldReturnMethodNotFound()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "mnf-01",
            Method = "nonexistent_method",
            Params = null
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.MethodNotFound);
        response.Error.Message.Should().Contain("nonexistent_method");
        response.Id.Should().Be("mnf-01");
    }

    // ── Successful dispatch (control case) ───────────────────────────────────

    [Fact]
    public async Task DispatchAsync_Ping_ShouldSucceedWithNullToken()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "ok-01",
            Method = "ping",
            Params = null
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
        response.Id.Should().Be("ok-01");
    }

    // ── Error response structure ──────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_ErrorResponse_ShouldHaveNullResult()
    {
        var dispatcher = CreateDispatcher();
        var request = new InspectorRequest
        {
            Id = "struct-01",
            Method = "execute_command",
            // No commandName → ArgumentException
            Params = JsonSerializer.SerializeToElement(new { elementId = "elem" })
        };

        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        response.Result.Should().BeNull("error responses must not have a result payload");
        response.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task DispatchAsync_CancellationErrorResponse_ShouldHaveNullResult()
    {
        var dispatcher = CreateDispatcher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new InspectorRequest
        {
            Id = "struct-02",
            Method = "get_visual_tree",
            Params = null
        };

        var response = await dispatcher.DispatchAsync(request, cts.Token);

        response.Result.Should().BeNull("cancelled responses must not have a result payload");
        response.Error.Should().NotBeNull();
    }

    // ── Multiple independent dispatches share no state ────────────────────────

    [Fact]
    public async Task DispatchAsync_MultipleCallsToSameDispatcher_AreIndependent()
    {
        var dispatcher = CreateDispatcher();

        var ping = new InspectorRequest { Id = "multi-1", Method = "ping", Params = null };
        var bad = new InspectorRequest
        {
            Id = "multi-2",
            Method = "execute_command",
            Params = JsonSerializer.SerializeToElement(new { elementId = "x" })
        };
        var unknown = new InspectorRequest { Id = "multi-3", Method = "no_such_method", Params = null };

        var pingResp = await dispatcher.DispatchAsync(ping, CancellationToken.None);
        var badResp = await dispatcher.DispatchAsync(bad, CancellationToken.None);
        var unknownResp = await dispatcher.DispatchAsync(unknown, CancellationToken.None);

        pingResp.Error.Should().BeNull();
        badResp.Error!.Code.Should().Be(ErrorCode.InvalidParams);
        unknownResp.Error!.Code.Should().Be(ErrorCode.MethodNotFound);

        pingResp.Id.Should().Be("multi-1");
        badResp.Id.Should().Be("multi-2");
        unknownResp.Id.Should().Be("multi-3");
    }
}
