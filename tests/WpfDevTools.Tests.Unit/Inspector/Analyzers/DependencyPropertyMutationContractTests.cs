using System.Text.Json;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Utilities;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public sealed class DependencyPropertyMutationContractTests : IDisposable
{
    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [StaFact]
    public void SetValue_ShouldReturnOldAndNewValueMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 120d };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.SetValue("Width", 240d, elementId)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.GetProperty("oldValue").GetString().Should().Be("120");
        result.GetProperty("newValue").GetString().Should().Be("240");
    }

    [StaFact]
    public void ClearValue_ShouldReturnClearedAndCurrentValueMetadata()
    {
        var finder = new ElementFinder();
        var analyzer = new DependencyPropertyAnalyzer(finder);
        var button = new Button { Width = 150d };
        var elementId = finder.GenerateElementId(button);

        var result = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(analyzer.ClearValue("Width", elementId)));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("propertyName").GetString().Should().Be("Width");
        result.TryGetProperty("clearedValue", out _).Should().BeTrue();
        result.TryGetProperty("newValue", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SetDpValue_Navigation_ShouldSuggestDpValueSourceVerification()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName = "Width",
                oldValue = "120",
                newValue = "240"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("propertyName", "Width"), ("value", 240)),
            CancellationToken.None,
            toolName: "set_dp_value");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_dp_value_source");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("Width");
    }

    [Fact]
    public async Task SetDpValue_Navigation_WithActiveSnapshot_ShouldIncludeStateDiffWorkflowHint()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName = "Width",
                oldValue = "120",
                newValue = "240"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "SaveButton"), ("propertyName", "Width"), ("value", 240)),
            CancellationToken.None,
            navigationState: new NavigationSessionState("snapshot_123", null),
            toolName: "set_dp_value");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps[1].GetProperty("tool").GetString().Should().Be("get_state_diff");
        nextSteps[1].GetProperty("preconditions")[0].GetString().Should().Be("activeSnapshot");
        nextSteps[1].GetProperty("workflowId").GetString().Should().Be("safe-mutation-loop");
    }
}
