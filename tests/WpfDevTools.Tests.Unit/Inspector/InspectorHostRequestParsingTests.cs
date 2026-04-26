using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("TimingSensitive")]
public sealed class InspectorHostRequestParsingTests
{
    [Fact]
    public async Task ValidRequest_WithCaseInsensitiveRequiredProperties_ShouldDeserialize()
    {
        var response = await SendRawRequestAsync("""{"Id":"case-ping","Method":"ping"}""");

        response.Id.Should().Be("case-ping");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
        response.Result!.Value.GetProperty("status").GetString().Should().Be("pong");
    }

    [Theory]
    [InlineData("""{"id":"missing-method"}""", "missing-method")]
    [InlineData("""{"id":"null-method","method":null}""", "null-method")]
    [InlineData("""{"id":"blank-method","method":""}""", "blank-method")]
    [InlineData("""{"id":"white-method","method":"   "}""", "white-method")]
    public async Task InvalidRequest_WithInvalidMethodShape_ShouldReturnInvalidRequestAndPreserveRequestId(
        string requestJson,
        string expectedRequestId)
    {
        var response = await SendRawRequestAsync(requestJson);

        response.Id.Should().Be(expectedRequestId);
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidRequest);
        response.Error.Message.Should().Contain("method");
    }

    [Theory]
    [InlineData("""{"method":"ping"}""")]
    [InlineData("""{"id":null,"method":"ping"}""")]
    [InlineData("""{"id":"","method":"ping"}""")]
    [InlineData("""{"id":"   ","method":"ping"}""")]
    public async Task InvalidRequest_WithInvalidIdShape_ShouldReturnInvalidRequestWithUnknownRequestId(
        string requestJson)
    {
        var response = await SendRawRequestAsync(requestJson);

        response.Id.Should().Be("unknown");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidRequest);
        response.Error.Message.Should().Contain("id");
    }

    private static async Task<InspectorResponse> SendRawRequestAsync(string requestJson)
    {
        var pid = TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000);
        await MessageFraming.WriteMessageAsync(client, requestJson);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var responseJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        response.Should().NotBeNull();
        return response!;
    }
}
