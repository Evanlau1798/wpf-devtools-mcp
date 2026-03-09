using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// E2E tests for MCP process management tools (get_processes, connect, ping).
/// Validates the full pipeline: MCP Server -> Named Pipes -> Injected Inspector -> WPF App.
/// </summary>
[Collection("McpE2E")]
[Trait("Category", "E2E")]
public sealed class ProcessManagementE2eTests
{
    private readonly McpE2eFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ProcessManagementE2eTests(McpE2eFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GetProcesses_WithNameFilter_ShouldFindTestApp()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "get_processes",
            new { nameFilter = "WpfDevTools.Tests.TestApp" });

        _output.WriteLine($"get_processes result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("processes").GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "at least one TestApp process should be detected");

        var firstProcess = result.GetProperty("processes")[0];
        firstProcess.GetProperty("processId").GetInt32().Should().Be(_fixture.TestAppProcessId);
        firstProcess.GetProperty("processName").GetString().Should().Contain("TestApp");
    }

    [Fact]
    public async Task GetProcesses_WithoutFilter_ShouldReturnMultipleProcesses()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync("get_processes");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("processes").GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "at least one WPF process should be detected when TestApp is running");
    }

    [Fact]
    public async Task Ping_ShouldSucceed_WhenConnected()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var result = await _fixture.Client.CallToolAsync(
            "ping",
            new { processId = _fixture.TestAppProcessId });

        _output.WriteLine($"ping result: {result.GetRawText()}");

        result.GetProperty("success").GetBoolean().Should().BeTrue(
            "ping should succeed after connect");
    }

    [Fact]
    public async Task ToolsList_ShouldContainExpectedTools()
    {
        E2eTestHelpers.AssertFixtureReady(_fixture);

        var response = await _fixture.Client.ListToolsAsync();

        response.TryGetProperty("result", out var result).Should().BeTrue(
            "tools/list should return a result");

        result.TryGetProperty("tools", out var tools).Should().BeTrue(
            "result should contain tools array");

        var toolNames = new List<string>();
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.TryGetProperty("name", out var name))
                toolNames.Add(name.GetString()!);
        }

        _output.WriteLine($"Found {toolNames.Count} tools: {string.Join(", ", toolNames.Take(10))}...");

        toolNames.Should().Contain("get_processes");
        toolNames.Should().Contain("connect");
        toolNames.Should().Contain("ping");
        toolNames.Should().Contain("get_visual_tree");
        toolNames.Should().Contain("get_binding_errors");
        toolNames.Should().Contain("get_viewmodel");
        toolNames.Should().Contain("click_element");
        toolNames.Should().Contain("get_render_stats");
    }
}
