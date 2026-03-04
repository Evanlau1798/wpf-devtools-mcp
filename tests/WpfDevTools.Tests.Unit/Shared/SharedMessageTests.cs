using System.Text.Json;
using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Shared;

/// <summary>
/// JSON serialization / deserialization round-trip tests for all shared message types.
/// Verifies JsonPropertyName attributes, null-safety for optional fields, and
/// that data round-trips without loss.
/// </summary>
public class SharedMessageTests
{
    // ── InspectorRequest ─────────────────────────────────────────────────────

    [Fact]
    public void InspectorRequest_WithAllFields_ShouldRoundTrip()
    {
        var request = new InspectorRequest
        {
            Id = "req-001",
            Method = "get_visual_tree",
            Params = JsonSerializer.SerializeToElement(new { depth = 3, filter = "Button" })
        };

        var json = JsonSerializer.Serialize(request);
        var result = JsonSerializer.Deserialize<InspectorRequest>(json);

        result.Should().NotBeNull();
        result!.Id.Should().Be("req-001");
        result.Method.Should().Be("get_visual_tree");
        result.Params.Should().NotBeNull();
        result.Params!.Value.GetProperty("depth").GetInt32().Should().Be(3);
        result.Params.Value.GetProperty("filter").GetString().Should().Be("Button");
    }

    [Fact]
    public void InspectorRequest_WithNullParams_ShouldRoundTrip()
    {
        var request = new InspectorRequest
        {
            Id = "req-002",
            Method = "ping",
            Params = null
        };

        var json = JsonSerializer.Serialize(request);
        var result = JsonSerializer.Deserialize<InspectorRequest>(json);

        result.Should().NotBeNull();
        result!.Id.Should().Be("req-002");
        result.Method.Should().Be("ping");
        result.Params.Should().BeNull();
    }

    [Fact]
    public void InspectorRequest_JsonPropertyNames_ShouldBeLowerCamelCase()
    {
        var request = new InspectorRequest
        {
            Id = "req-003",
            Method = "test_method",
            Params = null
        };

        var json = JsonSerializer.Serialize(request);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("id", out _).Should().BeTrue("'id' must use JsonPropertyName(\"id\")");
        doc.RootElement.TryGetProperty("method", out _).Should().BeTrue("'method' must use JsonPropertyName(\"method\")");
        doc.RootElement.TryGetProperty("params", out _).Should().BeTrue("'params' must use JsonPropertyName(\"params\")");
    }

    // ── InspectorResponse ────────────────────────────────────────────────────

    [Fact]
    public void InspectorResponse_WithSuccessResult_ShouldRoundTrip()
    {
        var response = new InspectorResponse
        {
            Id = "res-001",
            Result = JsonSerializer.SerializeToElement(new { success = true, count = 42 }),
            Error = null
        };

        var json = JsonSerializer.Serialize(response);
        var result = JsonSerializer.Deserialize<InspectorResponse>(json);

        result.Should().NotBeNull();
        result!.Id.Should().Be("res-001");
        result.Error.Should().BeNull();
        result.Result.Should().NotBeNull();
        result.Result!.Value.GetProperty("success").GetBoolean().Should().BeTrue();
        result.Result.Value.GetProperty("count").GetInt32().Should().Be(42);
    }

    [Fact]
    public void InspectorResponse_WithErrorAndNullResult_ShouldRoundTrip()
    {
        var response = new InspectorResponse
        {
            Id = "res-002",
            Result = null,
            Error = new InspectorError
            {
                Code = ErrorCode.MethodNotFound,
                Message = "Method not found: foo",
                Data = null
            }
        };

        var json = JsonSerializer.Serialize(response);
        var result = JsonSerializer.Deserialize<InspectorResponse>(json);

        result.Should().NotBeNull();
        result!.Id.Should().Be("res-002");
        result.Result.Should().BeNull();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(ErrorCode.MethodNotFound);
        result.Error.Message.Should().Be("Method not found: foo");
    }

    [Fact]
    public void InspectorResponse_JsonPropertyNames_ShouldBeLowerCamelCase()
    {
        var response = new InspectorResponse
        {
            Id = "res-003",
            Result = null,
            Error = null
        };

        var json = JsonSerializer.Serialize(response);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("id", out _).Should().BeTrue("'id' must use JsonPropertyName(\"id\")");
        doc.RootElement.TryGetProperty("result", out _).Should().BeTrue("'result' must use JsonPropertyName(\"result\")");
        doc.RootElement.TryGetProperty("error", out _).Should().BeTrue("'error' must use JsonPropertyName(\"error\")");
    }

    // ── InspectorError ───────────────────────────────────────────────────────

    [Fact]
    public void InspectorError_WithAllErrorCodes_ShouldRoundTrip()
    {
        var allCodes = new[]
        {
            ErrorCode.InvalidRequest,
            ErrorCode.MethodNotFound,
            ErrorCode.InvalidParams,
            ErrorCode.InternalError,
            ErrorCode.Timeout,
            ErrorCode.ElementNotFound,
            ErrorCode.InvalidElement
        };

        foreach (var code in allCodes)
        {
            var error = new InspectorError
            {
                Code = code,
                Message = $"Error: {code}",
                Data = null
            };

            var json = JsonSerializer.Serialize(error);
            var result = JsonSerializer.Deserialize<InspectorError>(json);

            result.Should().NotBeNull($"ErrorCode.{code} should round-trip");
            result!.Code.Should().Be(code);
            result.Message.Should().Be($"Error: {code}");
        }
    }

    [Fact]
    public void InspectorError_WithOptionalData_ShouldRoundTrip()
    {
        var error = new InspectorError
        {
            Code = ErrorCode.InvalidParams,
            Message = "Missing required parameter",
            Data = JsonSerializer.SerializeToElement(new { param = "elementId", received = (string?)null })
        };

        var json = JsonSerializer.Serialize(error);
        var result = JsonSerializer.Deserialize<InspectorError>(json);

        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Value.GetProperty("param").GetString().Should().Be("elementId");
    }

    [Fact]
    public void InspectorError_WithNullData_ShouldRoundTrip()
    {
        var error = new InspectorError
        {
            Code = ErrorCode.InternalError,
            Message = "Unexpected failure",
            Data = null
        };

        var json = JsonSerializer.Serialize(error);
        var result = JsonSerializer.Deserialize<InspectorError>(json);

        result.Should().NotBeNull();
        result!.Data.Should().BeNull();
    }

    [Fact]
    public void InspectorError_JsonPropertyNames_ShouldBeLowerCamelCase()
    {
        var error = new InspectorError
        {
            Code = ErrorCode.InternalError,
            Message = "Test",
            Data = null
        };

        var json = JsonSerializer.Serialize(error);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("code", out _).Should().BeTrue("'code' must use JsonPropertyName(\"code\")");
        doc.RootElement.TryGetProperty("message", out _).Should().BeTrue("'message' must use JsonPropertyName(\"message\")");
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue("'data' must use JsonPropertyName(\"data\")");
    }

    // ── EventMessage ─────────────────────────────────────────────────────────

    [Fact]
    public void EventMessage_WithSimpleData_ShouldRoundTrip()
    {
        var msg = new EventMessage
        {
            Type = "binding_error",
            Data = JsonSerializer.SerializeToElement(new { element = "Button1", property = "Content" })
        };

        var json = JsonSerializer.Serialize(msg);
        var result = JsonSerializer.Deserialize<EventMessage>(json);

        result.Should().NotBeNull();
        result!.Type.Should().Be("binding_error");
        result.Data.GetProperty("element").GetString().Should().Be("Button1");
        result.Data.GetProperty("property").GetString().Should().Be("Content");
    }

    [Fact]
    public void EventMessage_WithComplexNestedData_ShouldRoundTrip()
    {
        var msg = new EventMessage
        {
            Type = "property_changed",
            Data = JsonSerializer.SerializeToElement(new
            {
                elementId = "elem-42",
                property = "Visibility",
                oldValue = "Visible",
                newValue = "Collapsed",
                timestamp = "2026-03-04T00:00:00Z"
            })
        };

        var json = JsonSerializer.Serialize(msg);
        var result = JsonSerializer.Deserialize<EventMessage>(json);

        result.Should().NotBeNull();
        result!.Type.Should().Be("property_changed");
        result.Data.GetProperty("elementId").GetString().Should().Be("elem-42");
        result.Data.GetProperty("newValue").GetString().Should().Be("Collapsed");
    }

    [Fact]
    public void EventMessage_JsonPropertyNames_ShouldBeLowerCamelCase()
    {
        var msg = new EventMessage
        {
            Type = "test_event",
            Data = JsonSerializer.SerializeToElement(new { ok = true })
        };

        var json = JsonSerializer.Serialize(msg);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("type", out _).Should().BeTrue("'type' must use JsonPropertyName(\"type\")");
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue("'data' must use JsonPropertyName(\"data\")");
    }

    // ── Cross-type correlation ───────────────────────────────────────────────

    [Fact]
    public void RequestAndResponse_ShouldShareCorrelationId()
    {
        const string correlationId = "corr-xyz-789";

        var request = new InspectorRequest
        {
            Id = correlationId,
            Method = "ping",
            Params = null
        };

        var response = new InspectorResponse
        {
            Id = correlationId,
            Result = JsonSerializer.SerializeToElement(new { status = "pong" }),
            Error = null
        };

        request.Id.Should().Be(response.Id);
    }
}
