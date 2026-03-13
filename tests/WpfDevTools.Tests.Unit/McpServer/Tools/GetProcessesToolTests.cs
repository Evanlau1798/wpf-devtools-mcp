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
        var tool = new GetProcessesTool(new FakeProcessDetector(
        [
            CreateProcessInfo(42, "TestApp"),
            CreateProcessInfo(43, "DesignerHost")
        ]), () => false);
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var processes = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        processes.GetProperty("processes").EnumerateArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task Execute_WithNameFilter_ShouldReturnFilteredProcesses()
    {
        // Arrange
        var tool = new GetProcessesTool(new FakeProcessDetector(
        [
            CreateProcessInfo(42, "TestApp"),
            CreateProcessInfo(43, "DesignerHost")
        ]), () => false);
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
        var tool = new GetProcessesTool(new FakeProcessDetector([CreateProcessInfo(42, "TestApp")]), () => false);
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

    [Fact]
    public async Task Execute_WithoutWindowFilter_ShouldDefaultToVisible()
    {
        var detector = new FakeProcessDetector();
        var tool = new GetProcessesTool(detector, () => false);

        await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        detector.RequestedWindowFilters.Should().ContainSingle().Which.Should().Be(ProcessWindowFilter.Visible);
    }

    [Fact]
    public async Task Execute_WithWindowFilterAll_ShouldForwardAllFilter()
    {
        var detector = new FakeProcessDetector();
        var tool = new GetProcessesTool(detector, () => false);

        await tool.ExecuteAsync(ToJsonElement(new { windowFilter = "all" }), CancellationToken.None);

        detector.RequestedWindowFilters.Should().ContainSingle().Which.Should().Be(ProcessWindowFilter.All);
    }

    [Fact]
    public async Task Execute_WithInvalidWindowFilter_ShouldReturnValidationError()
    {
        var detector = new FakeProcessDetector();
        var tool = new GetProcessesTool(detector, () => false);

        var result = await tool.ExecuteAsync(ToJsonElement(new { windowFilter = "bad-filter" }), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
    }

    [Fact]
    public async Task Execute_WhenDetectorThrows_ShouldReturnStructuredErrorContract()
    {
        var tool = new GetProcessesTool(new ThrowingProcessDetector(), () => false);

        var result = await tool.ExecuteAsync(ToJsonElement(new { }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().NotBeNullOrWhiteSpace();
        json.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private sealed class FakeProcessDetector : WpfProcessDetector
    {
        private readonly IReadOnlyList<WpfProcessInfo> _processes;

        internal FakeProcessDetector()
            : this([CreateProcessInfo(42, "ElevatedTestApp", isElevated: true)])
        {
        }

        internal FakeProcessDetector(IReadOnlyList<WpfProcessInfo> processes)
        {
            _processes = processes;
        }

        internal List<ProcessWindowFilter> RequestedWindowFilters { get; } = [];

        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
        {
            RequestedWindowFilters.Add(windowFilter);
            return _processes;
        }
    }

    private sealed class ThrowingProcessDetector : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
            => throw new InvalidOperationException("simulated enumeration failure");
    }

    private static WpfProcessInfo CreateProcessInfo(int processId, string processName, bool isElevated = false)
        => new()
        {
            ProcessId = processId,
            ProcessName = processName,
            WindowTitle = processName + " Window",
            Architecture = ProcessArchitecture.X64,
            DotNetVersion = ".NET Core/5+",
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true,
            IsElevated = isElevated
        };
}
