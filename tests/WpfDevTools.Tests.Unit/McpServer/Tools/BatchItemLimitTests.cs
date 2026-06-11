using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchItemLimitTests
{
    [Fact]
    public void ParseElementTargets_WithTooManyPluralTargets_ShouldReturnStructuredError()
    {
        var elementIds = Enumerable.Range(0, 101)
            .Select(index => $"Button_{index}")
            .ToArray();
        var arguments = ToJsonElement(new { elementIds });

        var result = BatchQueryArgumentParser.ParseElementTargets(arguments, "elementId", "elementIds");

        result.Error.Should().NotBeNull();
        var json = JsonSerializer.SerializeToElement(result.Error);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("elementIds");
        json.GetProperty("error").GetString().Should().Contain("100");
        json.GetProperty("hint").GetString().Should().Contain("smaller batches");
        var errorData = json.GetProperty("errorData");
        errorData.GetProperty("parameter").GetString().Should().Be("elementIds");
        errorData.GetProperty("actualItems").GetInt32().Should().Be(101);
        errorData.GetProperty("maxItems").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_WithCrossProductAboveExpansionLimit_ShouldRejectWithoutQuerying()
    {
        var queryCalls = 0;

        var result = await BatchQueryExecutor.ExecuteAsync(
            Enumerable.Range(0, 51).Select(index => $"Button_{index}").ToArray(),
            new[] { "Width", "Height" },
            (_, _, _) =>
            {
                queryCalls++;
                return Task.FromResult<object>(new { success = true });
            },
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("100");
        json.GetProperty("hint").GetString().Should().Contain("smaller batches");
        json.GetProperty("errorData").GetProperty("actualItems").GetInt32().Should().Be(102);
        queryCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithPairwiseBroadcastAboveExpansionLimit_ShouldRejectWithoutQuerying()
    {
        var queryCalls = 0;

        var result = await BatchQueryExecutor.ExecuteAsync(
            Enumerable.Range(0, 101).Select(index => $"Button_{index}").ToArray(),
            new[] { "Width" },
            (_, _, _) =>
            {
                queryCalls++;
                return Task.FromResult<object>(new { success = true });
            },
            CancellationToken.None,
            BatchQueryExecutor.CombinationMode.PairwiseOrBroadcast);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("pairwise");
        json.GetProperty("error").GetString().Should().Contain("100");
        json.GetProperty("hint").GetString().Should().Contain("smaller batches");
        json.GetProperty("errorData").GetProperty("actualItems").GetInt32().Should().Be(101);
        queryCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithTooManyMutations_ShouldReturnStructuredErrorBeforeExecution()
    {
        var mutationCalled = false;
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) =>
            {
                mutationCalled = true;
                return Task.FromResult<object>(new { success = true });
            },
            null,
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                mutations = CreateMutationItems(101)
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("mutations");
        result.GetProperty("error").GetString().Should().Contain("100");
        result.GetProperty("hint").GetString().Should().Contain("smaller batches");
        var errorData = result.GetProperty("errorData");
        errorData.GetProperty("parameter").GetString().Should().Be("mutations");
        errorData.GetProperty("actualItems").GetInt32().Should().Be(101);
        errorData.GetProperty("maxItems").GetInt32().Should().Be(100);
        mutationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithTooManyStringifiedMutations_ShouldReturnStructuredErrorBeforeExecution()
    {
        var mutationCalled = false;
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) =>
            {
                mutationCalled = true;
                return Task.FromResult<object>(new { success = true });
            },
            null,
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                mutations = JsonSerializer.Serialize(CreateMutationItems(101))
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("errorData").GetProperty("actualItems").GetInt32().Should().Be(101);
        mutationCalled.Should().BeFalse();
    }

    private static object[] CreateMutationItems(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new
            {
                tool = "focus_element",
                args = new { elementId = $"Button_{index}" }
            })
            .Cast<object>()
            .ToArray();
}
