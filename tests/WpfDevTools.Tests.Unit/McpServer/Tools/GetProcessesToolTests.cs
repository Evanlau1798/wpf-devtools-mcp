using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class GetProcessesToolTests
{
    [Fact]
    public async Task Execute_WithNoFilter_ShouldReturnAllWpfProcesses()
    {
        // Arrange
        var tool = new GetProcessesTool();
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var processes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        processes.GetProperty("processes").EnumerateArray().Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_WithNameFilter_ShouldReturnFilteredProcesses()
    {
        // Arrange
        var tool = new GetProcessesTool();
        var parameters = new { nameFilter = "TestApp" };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var processes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var processArray = processes.GetProperty("processes").EnumerateArray().ToList();

        // All returned processes should match the filter
        foreach (var process in processArray)
        {
            var name = process.GetProperty("processName").GetString();
            name.Should().Contain("TestApp", "all processes should match the filter");
        }
    }

    [Fact]
    public async Task Execute_ShouldReturnProcessInfo()
    {
        // Arrange
        var tool = new GetProcessesTool();
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var processes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var processArray = processes.GetProperty("processes").EnumerateArray().ToList();

        if (processArray.Count > 0)
        {
            var firstProcess = processArray[0];
            firstProcess.TryGetProperty("processId", out _).Should().BeTrue();
            firstProcess.TryGetProperty("processName", out _).Should().BeTrue();
            firstProcess.TryGetProperty("windowTitle", out _).Should().BeTrue();
            firstProcess.TryGetProperty("architecture", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Execute_ShouldExposeElevationWarning_WhenCurrentServerIsNotElevated()
    {
        // Arrange
        var tool = new GetProcessesTool(new FakeProcessDetector(), () => false);

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        // Assert
        var processes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var firstProcess = processes.GetProperty("processes").EnumerateArray().Single();

        firstProcess.GetProperty("runtime").GetString().Should().Be("NetCore");
        firstProcess.GetProperty("isElevated").GetBoolean().Should().BeTrue();
        firstProcess.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeTrue();
        firstProcess.GetProperty("canConnectFromCurrentServer").GetBoolean().Should().BeFalse();
        firstProcess.GetProperty("connectionWarning").GetString().Should().Contain("administrator");
    }

    [Fact]
    public async Task Execute_ShouldNotExposeElevationWarning_WhenCurrentServerIsAlreadyElevated()
    {
        // Arrange
        var tool = new GetProcessesTool(new FakeProcessDetector(), () => true);

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        // Assert
        var processes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        var firstProcess = processes.GetProperty("processes").EnumerateArray().Single();

        firstProcess.GetProperty("isElevated").GetBoolean().Should().BeTrue();
        firstProcess.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeFalse();
        firstProcess.GetProperty("canConnectFromCurrentServer").GetBoolean().Should().BeTrue();
        firstProcess.GetProperty("connectionWarning").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private sealed class FakeProcessDetector : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses()
        {
            return
            [
                new WpfProcessInfo
                {
                    ProcessId = 42,
                    ProcessName = "ElevatedTestApp",
                    WindowTitle = "Elevated Test",
                    Architecture = ProcessArchitecture.X64,
                    DotNetVersion = ".NET Core/5+",
                    Runtime = TargetRuntime.NetCore,
                    IsWpfApplication = true,
                    IsElevated = true
                }
            ];
        }
    }
}
